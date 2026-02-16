using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;

namespace CargoSorter
{
    public class InventoryInfo
    {
        private static readonly char[] sectionEndCharacters = { '\r', '\n', ']' };
        private static readonly MyIni iniParser = new MyIni();
        public byte Priority;
        public TypeRequests TypeRequests;
        public Dictionary<MyDefinitionId, RequestData> Requests;
        public RequestValidationStatus RequestStatus;
        public Dictionary<MyDefinitionId, MyFixedPoint> VirtualInventory;
        public MyFixedPoint VirtualVolume;
        public MyFixedPoint VirtualMass;
        public readonly MyFixedPoint MaxVolume;
        public readonly MyFixedPoint MaxMass;
        public readonly MyInventoryConstraint Constraint;
        public readonly MyInventory RealInventory;
        public readonly IMyTerminalBlock Block;
        public readonly MyIniParseResult ConfigParseResult;
        public readonly bool SupportsConveyors;

        public InventoryInfo(MyInventory realInventory, string sectionName)
        {
            Block = realInventory.Entity as IMyTerminalBlock;
            Priority = byte.MaxValue;
            VirtualInventory = new Dictionary<MyDefinitionId, MyFixedPoint>(realInventory.GetItemsCount());
            VirtualVolume = realInventory.CurrentVolume;
            VirtualMass = realInventory.CurrentMass - realInventory.ExternalMass;
            MaxVolume = realInventory.MaxVolume;
            MaxMass = realInventory.MaxMass;
            Constraint = realInventory.Constraint;
            RealInventory = realInventory;
            // Require conveyors for weapons always since some weapons are balanced by being manually reloadable only.
            SupportsConveyors = CargoSorterSessionComponent.HasConveyorSupport(Block) || CargoSorterSessionComponent.Instance.IsWeapon(Block);

            foreach (var item in realInventory.GetItems())
            {
                MyFixedPoint amount;
                VirtualInventory.TryGetValue(item.Content.GetId(), out amount);
                amount += item.Amount;
                VirtualInventory[item.Content.GetId()] = amount;
            }

            var config = CargoSorterSessionComponent.Instance?.Config;
            if (config == null || Block == null)
            {
                TypeRequests = TypeRequests.Nothing;
                return;
            }

            if (Block.DisplayNameText.InsensitiveContains(config.SpecialContainerKeyword))
            {
                TypeRequests = TypeRequests.Special;
                ConfigParseResult = ParseCustomDataRequests(this, "Inventory");
            }
            else
            {
                if (Block.DisplayNameText.InsensitiveContains(config.AnyContainerKeyword))
                {
                    TypeRequests = TypeRequests.Ores | TypeRequests.Ingots | TypeRequests.Components | TypeRequests.Tools | TypeRequests.Ammo | TypeRequests.Bottles | TypeRequests.Consumables | TypeRequests.Ingredients;
                }
                else
                {
                    if (Block.DisplayNameText.InsensitiveContains(config.OreContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ores;
                    }

                    if (Block.DisplayNameText.InsensitiveContains(config.IngotContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ingots;
                    }

                    if (Block.DisplayNameText.InsensitiveContains(config.ComponentContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Components;
                    }

                    if (Block.DisplayNameText.InsensitiveContains(config.ToolContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Tools;
                    }

                    if (Block.DisplayNameText.InsensitiveContains(config.AmmoContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ammo;
                    }

                    if (Block.DisplayNameText.InsensitiveContains(config.BottleContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Bottles;
                    }

                    if (Block.DisplayNameText.InsensitiveContains(config.ConsumablesContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Consumables;
                    }

                    if (Block.DisplayNameText.InsensitiveContains(config.IngredientsContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ingredients;
                    }
                }

                if (Block.DisplayNameText.InsensitiveContains(config.LimitedContainerKeyword))
                {
                    TypeRequests |= TypeRequests.Limited;
                    ConfigParseResult = ParseCustomDataRequests(this, "Inventory");
                }
            }

            if (sectionName != "Inventory" && Block.CustomData.InsensitiveContains(sectionName))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort ({profile}): {Block.DisplayNameText}");
                TypeRequests = TypeRequests.Special;
                ConfigParseResult = ParseCustomDataRequests(this, sectionName);
            }

            if (Requests != null && Requests.Count > 0)
            {
                if (!CanRequestsFit(Requests, realInventory.MaxVolume))
                {
                    RequestStatus |= RequestValidationStatus.TooMuchVolume;
                }
            }

            var priorityStartIndex = Block.DisplayNameText.IndexOf("[P", StringComparison.OrdinalIgnoreCase);
            if (priorityStartIndex > -1)
            {
                priorityStartIndex += 2;
                var priorityLen = 0;
                bool foundTerminator = false;
                while (priorityStartIndex + priorityLen < Block.DisplayNameText.Length && priorityLen < 4)
                {
                    if (Block.DisplayNameText[priorityStartIndex + priorityLen] == ']')
                    {
                        foundTerminator = true;
                        break;
                    }

                    priorityLen++;
                }

                if (priorityLen > 0 && foundTerminator)
                {
                    if (!byte.TryParse(Block.DisplayNameText.Substring(priorityStartIndex, priorityLen), out Priority))
                    {
                        Priority = byte.MaxValue;
                    }
                }
            }

            if (TypeRequests.Equals(TypeRequests.Nothing) && Priority == byte.MaxValue)
            {
                if (Block is IMyGasGenerator)
                {
                    TypeRequests = TypeRequests.GasGeneratorOre;
                    Priority = 0;
                }
                else if (Block is IMyAssembler) // Survival kits are OK here too
                {
                    TypeRequests = TypeRequests.AssemblerIngots;
                    Priority = 0;

                    if (!Block.CustomData.Contains("[Inventory]"))
                    {
                        return;
                    }

                    Requests = new Dictionary<MyDefinitionId, RequestData>();
                    ConfigParseResult = ParseCustomDataRequests(this, "Inventory");
                }
                else if (Block is IMyRefinery)
                {
                    TypeRequests = TypeRequests.RefineryOre;
                    Priority = 0;
                }
                else if (Block is IMyGasTank)
                {
                    TypeRequests = TypeRequests.GasTankBottles;
                }
                else if (Block is IMyReactor)
                {
                    TypeRequests = TypeRequests.ReactorFuel;
                    Priority = 0;
                }
                else if (CargoSorterSessionComponent.Instance.IsWeapon(Block) || Block is IMyParachute)
                {
                    TypeRequests = TypeRequests.ConsumableAmmo;
                    Priority = 0;
                }
                else if (Block is IMyConveyorSorter)
                {
                    TypeRequests = TypeRequests.SorterItems;
                    Priority = 0;
                }
            }
            //MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.DisplayNameText} wants {TypeRequests} with priority {Priority}");
        }

        private bool CanRequestsFit(Dictionary<MyDefinitionId, RequestData> requests, MyFixedPoint maxVolume)
        {
            MyFixedPoint sumVolume = MyFixedPoint.Zero;
            foreach (var request in requests)
            {
                if (request.Value.Flag == RequestFlags.All) // It's an All request, skip it.
                {
                    continue;
                }

                float itemVolume;
                if (CargoSorterSessionComponent.Instance.TryGetVolume(request.Key, out itemVolume))
                {
                    sumVolume += request.Value.Amount * itemVolume;
                }
            }

            return sumVolume <= maxVolume;
        }

        private MyIniParseResult ParseCustomDataRequests(InventoryInfo inventoryInfo, string sectionName)
        {
            MyIniParseResult quotaParseResult = new MyIniParseResult();
            if (!Util.IsValid(inventoryInfo.Block))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} isn't a terminal block");
                return quotaParseResult;
            }

            iniParser.Clear();
            if (IsCustomDataEmpty(Block.CustomData))
            {
                Block.CustomData = BuildCurrentContentsSpecialData(Block, sectionName, iniParser);
            }
            else if (!iniParser.TryParse(Block.CustomData, out quotaParseResult))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} failed to parse customdata into Special config");
                inventoryInfo.RequestStatus |= RequestValidationStatus.InvalidCustomData;
                return quotaParseResult;
            }

            if (!iniParser.ContainsSection(sectionName))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.DisplayNameText} has no {sectionName} config section");
                Block.CustomData = BuildCurrentContentsSpecialData(Block, sectionName, iniParser);
            }

            List<MyIniKey> iniKeys = new List<MyIniKey>();
            iniParser.GetKeys(sectionName, iniKeys);

            if (inventoryInfo.Requests == null)
            {
                inventoryInfo.Requests = new Dictionary<MyDefinitionId, RequestData>();
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} has {iniKeys.Count}");
            foreach (var iniKey in iniKeys)
            {
                if (iniKey.IsEmpty)
                {
                    continue;
                }

                // Allow forcing a new priority with a special key
                if (iniKey.Name.Equals("Priority", StringComparison.OrdinalIgnoreCase))
                {
                    var newPriority = iniParser.Get(iniKey).ToByte(byte.MaxValue);
                    inventoryInfo.Priority = newPriority;
                    continue;
                }

                MyDefinitionId definitionId;
                if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                {
                    inventoryInfo.RequestStatus |= RequestValidationStatus.InvalidItem;
                    continue;
                }

                var value = iniParser.Get(iniKey);
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} key {iniKey.Name} {value}");
                var valueString = value.ToString();
                if (string.IsNullOrWhiteSpace(valueString))
                {
                    inventoryInfo.Requests[definitionId] = new RequestData(0, RequestFlags.None);
                }
                else
                {
                    if (valueString.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        inventoryInfo.Requests[definitionId] = new RequestData(ComputeAmountThatCouldFit(definitionId), RequestFlags.All);
                        continue;
                    }

                    int itemCount;
                    if (!int.TryParse(valueString.TrimEnd('l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                    {
                        inventoryInfo.RequestStatus |= RequestValidationStatus.InvalidCount;
                        continue;
                    }

                    var requestValue = new RequestData(itemCount, RequestFlags.None);

                    var lastChar = valueString[valueString.Length - 1];
                    if (lastChar == 'L' || lastChar == 'l')
                    {
                        requestValue.Flag = RequestFlags.Limit;
                    }
                    else if (lastChar == 'M' || lastChar == 'm')
                    {
                        requestValue.Flag = RequestFlags.Minimum;
                    }

                    inventoryInfo.Requests[definitionId] = requestValue;
                }
            }

            iniParser.Clear();
            return quotaParseResult;
        }

        private bool IsCustomDataEmpty(string customData)
        {
            return string.IsNullOrWhiteSpace(customData) || customData.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase) || customData.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildCurrentContentsSpecialData(IMyCubeBlock block, string sectionName, MyIni ini)
        {
            var items = new Dictionary<MyDefinitionId, MyFixedPoint>();
            for (int i = 0; i < block.InventoryCount; i++)
            {
                var inv = (MyInventory)block.GetInventory(i);
                foreach (var item in inv.GetItems())
                {
                    MyDefinitionId id = item.Content.GetId();
                    MyFixedPoint amount;
                    items.TryGetValue(id, out amount);
                    amount += item.Amount;
                    items[id] = amount;
                }
            }

            if (items.Count == 0)
            {
                return string.Empty;
            }

            return BuildCustomData(items, false, sectionName, ini);
        }

        internal bool CanItemsFit(MyFixedPoint amount, MyDefinitionId itemDefinition, out MyFixedPoint volumeToBeMoved, out MyFixedPoint massToBeMoved)
        {
            float mass;
            float volume;
            bool hasIntegralAmounts;
            if (!CargoSorterSessionComponent.TryGetPhysicalItemProperties(itemDefinition, out volume, out mass, out hasIntegralAmounts))
            {
                volumeToBeMoved = 0;
                massToBeMoved = 0;
                return false;
            }

            if (hasIntegralAmounts)
            {
                amount = MyFixedPoint.Floor((MyFixedPoint)(Math.Round((double)amount * 1000.0) / 1000.0));
            }

            volumeToBeMoved = amount * volume;
            massToBeMoved = amount * mass;
            return !(volumeToBeMoved + VirtualVolume > MaxVolume) && !(massToBeMoved + VirtualMass > MaxMass);
        }

        internal MyFixedPoint ComputeAmountThatFits(MyDefinitionId contentId, bool forceIntegralAmount = false)
        {
            float mass;
            float volume;
            bool hasIntegralAmounts;
            if (!CargoSorterSessionComponent.TryGetPhysicalItemProperties(contentId, out volume, out mass, out hasIntegralAmounts))
            {
                return MyFixedPoint.Zero;
            }

            MyFixedPoint a = MyFixedPoint.Max((MyFixedPoint)(((double)MaxVolume - (double)VirtualVolume) / (double)volume), 0);
            MyFixedPoint b = MyFixedPoint.Max((MyFixedPoint)(((double)MaxMass - (double)VirtualMass) / (double)mass), 0);
            MyFixedPoint amount = MyFixedPoint.Min(a, b);
            if (hasIntegralAmounts || forceIntegralAmount)
            {
                amount = MyFixedPoint.Floor((MyFixedPoint)(Math.Round((double)amount * 1000.0) / 1000.0));
            }

            return amount;
        }

        internal MyFixedPoint ComputeAmountThatCouldFit(MyDefinitionId contentId, bool forceIntegralAmount = false, float volumeReserved = 0f, float massReserved = 0f)
        {
            float mass;
            float volume;
            bool hasIntegralAmounts;
            if (!CargoSorterSessionComponent.TryGetPhysicalItemProperties(contentId, out volume, out mass, out hasIntegralAmounts))
            {
                return MyFixedPoint.Zero;
            }

            MyFixedPoint a = MyFixedPoint.Max((MyFixedPoint)(((double)MaxVolume - (double)volumeReserved) / (double)mass), 0);
            MyFixedPoint b = MyFixedPoint.Max((MyFixedPoint)(((double)MaxMass - (double)massReserved) / (double)volume), 0);
            MyFixedPoint myFixedPoint = MyFixedPoint.Min(a, b);
            if (hasIntegralAmounts || forceIntegralAmount)
            {
                myFixedPoint = MyFixedPoint.Floor((MyFixedPoint)(Math.Round((double)myFixedPoint * 1000.0) / 1000.0));
            }

            return myFixedPoint;
        }

        internal static string BuildCustomData(Dictionary<MyDefinitionId, MyFixedPoint> items, bool ceiling, string sectionName = null, MyIni ini = null)
        {
            if (ini == null)
            {
                ini = new MyIni();
            }

            if (string.IsNullOrEmpty(sectionName))
            {
                sectionName = "Inventory";
            }

            ini.AddSection(sectionName);
            foreach (var item in items
                         .Select(i => new KeyValuePair<string, int>(
                             CargoSorterSessionComponent.Instance.GetFriendlyDefinitionName(i.Key),
                             (ceiling ? MyFixedPoint.Ceiling(i.Value) : i.Value).ToIntSafe()))
                         .OrderBy(i => i.Key))
            {
                ini.Set(sectionName, item.Key, item.Value);
            }

            return ini.ToString();
        }
    }
}