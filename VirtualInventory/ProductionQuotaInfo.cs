using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;

namespace InventorySorter.VirtualInventory
{
    public class ProductionQuotaInfo
    {
        private const string PrimaryTag = "[Primary:";
        public const string QuotaSectionName = "Quota";
        private const string OptionsSectionName = "QuotaOptions";

        private static readonly MyIni IniParser = new MyIni();

        // Using a list for this because the item order is implicit priority
        public RequestValidationStatus RequestStatus;
        public readonly List<AssemblerQuotaItem> QuotaItems = new List<AssemblerQuotaItem>();
        public readonly string GroupName;
        public MyIniParseResult ConfigParseResult;

        public ProductionQuotaInfo(IMyAssembler block)
        {
            // Determine if we have a quota group as part of the assembler name
            var groupStartIndex = block.CustomName.IndexOf(PrimaryTag, StringComparison.Ordinal);
            if (groupStartIndex > -1)
            {
                groupStartIndex += PrimaryTag.Length;
                var groupEnd = block.CustomName.IndexOf("]", groupStartIndex, StringComparison.Ordinal);
                if (groupEnd > 0)
                {
                    GroupName = block.CustomName.Substring(groupStartIndex, groupEnd - groupStartIndex);
                }
            }
        }

        public static MyIniParseResult Parse(IMyAssembler block)
        {
            MyIniParseResult parseResult;
            IniParser.Clear();
            IniParser.TryParse(block.CustomData, out parseResult);
            return parseResult;
        }

        public static AssemblerQuotaInfo ReadOptions(IMyAssembler block)
        {
            var result = new AssemblerQuotaInfo(block);

            if (IsCustomDataEmpty(block.CustomData) || !IniParser.ContainsSection(OptionsSectionName))
            {
                return result;
            }

            List<MyIniKey> iniKeys = new List<MyIniKey>();
            IniParser.GetKeys(OptionsSectionName, iniKeys);
            if (!IniParser.Get(OptionsSectionName, "AllowAssembly").TryGetBoolean(out result.AllowAssembly))
            {
                result.AllowAssembly = true;
            }

            if (!IniParser.Get(OptionsSectionName, "AllowDisassembly").TryGetBoolean(out result.AllowDisassembly))
            {
                result.AllowDisassembly = false;
            }

            if (!IniParser.Get(OptionsSectionName, "ClearQueue").TryGetBoolean(out result.ClearQueue))
            {
                result.ClearQueue = true;
            }

            return result;
        }

        public static void ReadQuota(IMyAssembler block, ProductionQuotaInfo info)
        {
            if (IsCustomDataEmpty(block.CustomData) || !IniParser.ContainsSection(QuotaSectionName))
            {
                return;
            }

            var iniKeys = new List<MyIniKey>();
            IniParser.GetKeys(QuotaSectionName, iniKeys);

            foreach (var iniKey in iniKeys)
            {
                if (iniKey.IsEmpty)
                {
                    continue;
                }

                MyDefinitionId definitionId;
                if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                {
                    info.RequestStatus |= RequestValidationStatus.InvalidItem;
                    continue;
                }

                var value = IniParser.Get(iniKey);
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} key {iniKey.Name} {value}");
                var valueString = value.ToString();
                if (string.IsNullOrWhiteSpace(valueString))
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {block.DisplayNameText} key {iniKey.Name} has an empty value, skipping");
                    continue;
                }

                var rangeIndex = valueString.IndexOf('-');
                if (rangeIndex != -1)
                {
                    int min, max;
                    if (!int.TryParse(valueString.Substring(0, rangeIndex), out min) || min < 0 || !int.TryParse(valueString.Substring(rangeIndex + 1), out max) || max < min)
                    {
                        info.RequestStatus |= RequestValidationStatus.InvalidCount;
                        continue;
                    }

                    var quotaItem = new AssemblerQuotaItem(definitionId, min, max);
                    info.QuotaItems.Add(quotaItem);
                }
                else
                {
                    int itemCount;
                    if (!int.TryParse(valueString.TrimEnd('%', 'l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                    {
                        info.RequestStatus |= RequestValidationStatus.InvalidCount;
                        continue;
                    }

                    var lastChar = valueString[valueString.Length - 1];
                    if (lastChar == 'L' || lastChar == 'l')
                    {
                        var quotaItem = new AssemblerQuotaItem(definitionId, 0, itemCount);
                        info.QuotaItems.Add(quotaItem);
                    }
                    else if (lastChar == 'M' || lastChar == 'm')
                    {
                        var quotaItem = new AssemblerQuotaItem(definitionId, itemCount, MyFixedPoint.MaxIntValue);
                        info.QuotaItems.Add(quotaItem);
                    }
                    else
                    {
                        var quotaItem = new AssemblerQuotaItem(definitionId, itemCount, itemCount);
                        info.QuotaItems.Add(quotaItem);
                    }
                }
            }
        }

        private static bool IsCustomDataEmpty(string customData) { return string.IsNullOrWhiteSpace(customData) || customData.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase) || customData.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase); }
    }

    public struct AssemblerQuotaItem
    {
        public readonly MyDefinitionId ItemId;
        public readonly MyFixedPoint Amount;
        public readonly MyFixedPoint Deviation;

        public AssemblerQuotaItem(MyDefinitionId itemId, MyFixedPoint min, MyFixedPoint max) : this()
        {
            ItemId = itemId;
            Amount = min;
            Deviation = max - min;
        }
    }
}