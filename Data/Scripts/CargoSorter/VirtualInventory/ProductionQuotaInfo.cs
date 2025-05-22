using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Input;
using VRage.Utils;

namespace CargoSorter
{
    public class ProductionQuotaInfo
    {
        public static readonly string QuotaSectionName = "Quota";
        public static readonly string OptionsSectionName = "QuotaOptions";
        private static readonly MyIni iniParser = new MyIni();
        // Using a list for this because the item order is implicit priority
        public RequestValidationStatus RequestStatus;
        public List<AssemblerQuotaItem> QuotaItems;
        public readonly string GroupName;
        public readonly MyIniParseResult ConfigParseResult;
        public ProductionQuotaInfo(IMyAssembler block)
        {
            // Check to see if the block even has quota data. If it doesn't, there's nothing to do!
            if (!block.CustomData.Contains("[Quota]"))
            {
                RequestStatus = RequestValidationStatus.InvalidCustomData;
                return;
            }
            // Determine if we have a quota group as part of the assembler name
            const string tag = "[Primary:";
            var groupStartIndex = block.DisplayNameText.IndexOf(tag);
            if (groupStartIndex > -1)
            {
                groupStartIndex += tag.Length;
                var groupEnd = block.DisplayNameText.IndexOf("]", groupStartIndex);
                if (groupEnd > 0)
                {
                    GroupName = block.DisplayNameText.Substring(groupStartIndex, groupEnd - groupStartIndex);
                }
            }

            ConfigParseResult = ParseQuota(block);
        }

        public static AssemblerQuotaInfo ParseQuotaOptions(IMyAssembler block)
        {
            iniParser.Clear();

            var result = new AssemblerQuotaInfo(block);
            if (!iniParser.TryParse(block.CustomData))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {block.DisplayNameText} failed to parse customdata into Quota Options config");
                return result;
            }

            if (IsCustomDataEmpty(block.CustomData) || !iniParser.ContainsSection(OptionsSectionName))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {block.DisplayNameText} using default Quota Options config");
                return result;
            }

            List<MyIniKey> iniKeys = new List<MyIniKey>();
            iniParser.GetKeys(OptionsSectionName, iniKeys);
            if (!iniParser.Get(OptionsSectionName, "AllowAssembly").TryGetBoolean(out result.AllowAssembly)) { result.AllowAssembly = true; }
            if (!iniParser.Get(OptionsSectionName, "AllowDisassembly").TryGetBoolean(out result.AllowDisassembly)) { result.AllowDisassembly = false; }
            if (!iniParser.Get(OptionsSectionName, "ClearQueue").TryGetBoolean(out result.ClearQueue)) { result.ClearQueue = true; }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {block.DisplayNameText} Quota Options config: AllowAssembly: {result.AllowAssembly}, AllowDisassembly: {result.AllowDisassembly}, ClearQueue: {result.ClearQueue}");
            return result;
        }

        private MyIniParseResult ParseQuota(IMyAssembler block)
        {
            MyIniParseResult quotaParseResult = new MyIniParseResult();
            iniParser.Clear();
            if (IsCustomDataEmpty(block.CustomData) || !iniParser.TryParse(block.CustomData, out quotaParseResult))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {block.DisplayNameText} failed to parse customdata into Quota config:\n{quotaParseResult.Error}");
                RequestStatus |= RequestValidationStatus.InvalidCustomData;
                return quotaParseResult;
            }
            List<MyIniKey> iniKeys = new List<MyIniKey>();
            iniParser.GetKeys(QuotaSectionName, iniKeys);

            if (QuotaItems == null)
            {
                QuotaItems = new List<AssemblerQuotaItem>();
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {block.DisplayNameText} has {iniKeys.Count}");
            foreach (var iniKey in iniKeys)
            {
                if (iniKey.IsEmpty)
                {
                    continue;
                }
                MyDefinitionId definitionId;
                if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                {
                    RequestStatus |= RequestValidationStatus.InvalidItem;
                    continue;
                }

                var value = iniParser.Get(iniKey);
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} key {iniKey.Name} {value}");
                var valueString = value.ToString();
                if (string.IsNullOrWhiteSpace(valueString))
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {block.DisplayNameText} key {iniKey.Name} has an empty value, skipping");
                    continue;
                }
                else
                {
                    int itemCount;
                    if (!int.TryParse(valueString.TrimEnd('l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                    {
                        RequestStatus |= RequestValidationStatus.InvalidCount;
                        continue;
                    }

                    var quotaItem = new AssemblerQuotaItem(definitionId, itemCount, RequestFlags.None);
                    var lastChar = valueString[valueString.Length - 1];
                    if (lastChar == 'L' || lastChar == 'l')
                    {
                        quotaItem.Flag = RequestFlags.Limit;
                    }
                    else if (lastChar == 'M' || lastChar == 'm')
                    {
                        quotaItem.Flag = RequestFlags.Minimum;
                    }
                    QuotaItems.Add(quotaItem);
                }
            }

            iniParser.Clear();
            return quotaParseResult;
        }

        private static bool IsCustomDataEmpty(string customData)
        {
            return string.IsNullOrWhiteSpace(customData) || customData.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase) || customData.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase);
        }
    }
    public struct AssemblerQuotaItem
    {
        public readonly MyDefinitionId ItemId;
        public readonly MyFixedPoint Amount;
        public RequestFlags Flag;

        public AssemblerQuotaItem(MyDefinitionId itemId, MyFixedPoint amount, RequestFlags flag) : this()
        {
            ItemId = itemId;
            Amount = amount;
            Flag = flag;
        }
    }
}
