using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.ObjectBuilders;

namespace CargoSorter
{
    public class InventoryInfo
    {
        public static readonly MyFixedPoint FixedPointFlagSpecialMinimum = MyFixedPoint.SmallestPossibleValue * (1 << 0x3);
        public static readonly MyFixedPoint FixedPointFlagSpecialLimited = MyFixedPoint.SmallestPossibleValue * (1 << 0x4);

        public byte Priority;
        public TypeRequests TypeRequests;
        public Dictionary<MyDefinitionId, MyFixedPoint> Requests;
        public RequestValidationStatus RequestStatus;
        public Dictionary<MyDefinitionId, MyFixedPoint> VirtualInventory;
        public MyFixedPoint VirtualVolume;
        public MyFixedPoint VirtualMass;
        public readonly MyFixedPoint MaxVolume;
        public readonly MyFixedPoint MaxMass;
        public readonly bool IsConstrained;
        public readonly MyInventoryConstraint Constraint;
        public readonly MyInventory RealInventory;
        public readonly IMyCubeBlock Block;

        public InventoryInfo(MyInventory realInventory)
        {
            Block = realInventory.Entity as IMyCubeBlock;
            Priority = byte.MaxValue;
            VirtualInventory = new Dictionary<MyDefinitionId, MyFixedPoint>(realInventory.GetItemsCount());
            VirtualVolume = realInventory.CurrentVolume;
            VirtualMass = realInventory.CurrentMass - realInventory.ExternalMass;
            MaxVolume = realInventory.MaxVolume;
            MaxMass = realInventory.MaxMass;
            IsConstrained = realInventory.IsConstrained;
            Constraint = realInventory.Constraint;
            RealInventory = realInventory;

            foreach (var item in realInventory.GetItems())
            {
                MyFixedPoint amount;
                VirtualInventory.TryGetValue(item.Content.GetId(), out amount);
                amount += item.Amount;
                VirtualInventory[item.Content.GetId()] = amount;
            }

            var config = CargoSorterSessionComponent.Instance?.Config;
            if (config == null)
            {
                TypeRequests = TypeRequests.Nothing;
                return;
            }

            if (Block.DisplayNameText.Contains(config.SpecialContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
            {
                TypeRequests = TypeRequests.Special;
                Requests = new Dictionary<MyDefinitionId, MyFixedPoint>();
                ParseCustomDataRequests(this, Requests);
            }
            else
            {
                if (Block.DisplayNameText.Contains(config.OreContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    TypeRequests |= TypeRequests.Ores;
                }
                if (Block.DisplayNameText.Contains(config.IngotContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    TypeRequests |= TypeRequests.Ingots;
                }
                if (Block.DisplayNameText.Contains(config.ComponentContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    TypeRequests |= TypeRequests.Components;
                }
                if (Block.DisplayNameText.Contains(config.ToolContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    TypeRequests |= TypeRequests.Tools;
                }
                if (Block.DisplayNameText.Contains(config.AmmoContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    TypeRequests |= TypeRequests.Ammo;
                }
                if (Block.DisplayNameText.Contains(config.BottleContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    TypeRequests |= TypeRequests.Bottles;
                }
                if (Block.DisplayNameText.Contains(config.LimitedContainerKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    TypeRequests |= TypeRequests.Limited;
                    Requests = new Dictionary<MyDefinitionId, MyFixedPoint>();
                    ParseCustomDataRequests(this, Requests);
                }
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
                else if (Block is IMyUserControllableGun || Block is IMyParachute)
                {
                    TypeRequests = TypeRequests.ConsumableAmmo;
                    Priority = 0;
                }
                else if (Block is IMyConveyorSorter)
                {
                    // If it doesn't contain Sorter in the name, it's probably a weaponcore weapon.
                    TypeRequests = Block.BlockDefinition.SubtypeId.Contains("Sorter") ? TypeRequests.SorterItems : TypeRequests.ConsumableAmmo;
                    Priority = 0;
                }
            }
            //MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.DisplayNameText} wants {TypeRequests} with priority {Priority}");
        }

        private bool CanRequestsFit(Dictionary<MyDefinitionId, MyFixedPoint> requests, MyFixedPoint maxVolume)
        {
            MyFixedPoint sumVolume = MyFixedPoint.Zero;
            foreach (var request in requests)
            {
                if (request.Value == MyFixedPoint.MaxValue) // It's an All request, skip it.
                {
                    continue;
                }

                float itemVolume;
                if (CargoSorterSessionComponent.Instance.TryGetVolume(request.Key, out itemVolume)) {
                    sumVolume += request.Value * itemVolume;
                }
            }
            return sumVolume <= maxVolume;
        }

        private void ParseCustomDataRequests(InventoryInfo inventoryInfo, Dictionary<MyDefinitionId, MyFixedPoint> specialRequests)
        {
            var terminalBlock = inventoryInfo.Block as IMyTerminalBlock;
            if (!Util.IsValid(terminalBlock))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} isn't a terminal block");
                return;
            }
            var ini = new MyIni();
            if (!ini.TryParse(terminalBlock.CustomData))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} failed to parse customdata into Special config");
                if (string.IsNullOrWhiteSpace(terminalBlock.CustomData))
                {
                    terminalBlock.CustomData = BuildCurrentContentsSpecialData(terminalBlock, ini);
                }
                else
                {
                    inventoryInfo.RequestStatus |= RequestValidationStatus.InvalidCustomData;
                    return;
                }
            }
            if (!ini.ContainsSection("Inventory"))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} has no Inventory config section");
                terminalBlock.CustomData = BuildCurrentContentsSpecialData(terminalBlock, ini);
            }
            List<MyIniKey> iniKeys = new List<MyIniKey>();
            ini.GetKeys("Inventory", iniKeys);

            //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} has {iniKeys.Count}");
            foreach (var iniKey in iniKeys)
            {
                if (iniKey.IsEmpty)
                {
                    continue;
                }
                MyDefinitionId definitionId;
                if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                {
                    inventoryInfo.RequestStatus |= RequestValidationStatus.InvalidItem;
                    continue;
                }

                var value = ini.Get(iniKey);
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {block.DisplayNameText} key {iniKey.Name} {value}");
                var valueString = value.ToString();
                if (string.IsNullOrWhiteSpace(valueString))
                {
                    specialRequests[definitionId] = 0;
                }
                else
                {
                    if (valueString.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        specialRequests[definitionId] = MyFixedPoint.MaxValue;
                        continue;
                    }

                    int itemCount;
                    if (!int.TryParse(valueString.TrimEnd('l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                    {
                        inventoryInfo.RequestStatus |= RequestValidationStatus.InvalidCount;
                        continue;
                    }

                    var fixedPointValue = (MyFixedPoint)itemCount;

                    var lastChar = valueString[valueString.Length - 1];
                    if (lastChar == 'L' || lastChar == 'l')
                    {
                        fixedPointValue += FixedPointFlagSpecialLimited;

                    }
                    else if (lastChar == 'M' || lastChar == 'm')
                    {
                        fixedPointValue += FixedPointFlagSpecialMinimum;
                    }
                    specialRequests[definitionId] = fixedPointValue;
                }
            }
        }

        private string BuildCurrentContentsSpecialData(IMyCubeBlock block, MyIni ini)
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

            return BuildCustomData(items, ini);
        }

        internal bool CanItemsBeAdded(MyFixedPoint amount, MyDefinitionId itemDefinition, out MyFixedPoint volumeToBeMoved, out MyFixedPoint massToBeMoved)
        {
            MyPhysicalItemDefinition physItem;
            if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(itemDefinition, out physItem))
            {
                volumeToBeMoved = 0;
                massToBeMoved = 0;
                return false;
            }
            volumeToBeMoved = amount * physItem.Volume;
            massToBeMoved = amount * physItem.Mass;
            return (!IsConstrained || !(volumeToBeMoved + VirtualVolume > MaxVolume)) && !(massToBeMoved + VirtualMass > MaxMass);
        }

        internal MyFixedPoint ComputeAmountThatFits(MyDefinitionId contentId, bool forceIntegralAmount = false)
        {
            if (!IsConstrained)
            {
                return MyFixedPoint.MaxValue;
            }
            MyPhysicalItemDefinition physItem;
            if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(contentId, out physItem))
            {
                return MyFixedPoint.Zero;
            }
            MyFixedPoint a = MyFixedPoint.Max((MyFixedPoint)(((double)MaxVolume - (double)VirtualVolume) / (double)physItem.Volume), 0);
            MyFixedPoint b = MyFixedPoint.Max((MyFixedPoint)(((double)MaxMass - (double)VirtualMass) / (double)physItem.Mass), 0);
            MyFixedPoint myFixedPoint = MyFixedPoint.Min(a, b);
            if (physItem.HasIntegralAmounts || forceIntegralAmount)
            {
                myFixedPoint = MyFixedPoint.Floor((MyFixedPoint)(Math.Round((double)myFixedPoint * 1000.0) / 1000.0));
            }
            return myFixedPoint;
        }

        internal MyFixedPoint ComputeAmountThatCouldFit(MyDefinitionId contentId, bool forceIntegralAmount = false, float volumeReserved = 0f, float massReserved = 0f)
        {
            if (!IsConstrained)
            {
                return MyFixedPoint.MaxValue;
            }
            MyPhysicalItemDefinition physItem;
            if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(contentId, out physItem))
            {
                return MyFixedPoint.Zero;
            }
            MyFixedPoint a = MyFixedPoint.Max((MyFixedPoint)(((double)MaxVolume - (double)volumeReserved) / (double)physItem.Volume), 0);
            MyFixedPoint b = MyFixedPoint.Max((MyFixedPoint)(((double)MaxMass - (double)massReserved) / (double)physItem.Mass), 0);
            MyFixedPoint myFixedPoint = MyFixedPoint.Min(a, b);
            if (physItem.HasIntegralAmounts || forceIntegralAmount)
            {
                myFixedPoint = MyFixedPoint.Floor((MyFixedPoint)(Math.Round((double)myFixedPoint * 1000.0) / 1000.0));
            }
            return myFixedPoint;
        }

        internal static string BuildCustomData(Dictionary<MyDefinitionId, MyFixedPoint> items, MyIni ini = null)
        {
            if (ini == null)
            {
                ini = new MyIni();
            }
            ini.AddSection("Inventory");
            foreach (var item in items)
            {
                string customDataKey;
                string friendlyType;
                if (CargoSorterSessionComponent.Instance.TryGetFriendlyName(item.Key, out friendlyType))
                {
                    customDataKey = $"{friendlyType}/{item.Key.SubtypeName}";
                }
                else
                {
                    customDataKey = item.Key.ToString().Replace(MyObjectBuilderType.LEGACY_TYPE_PREFIX, "");
                }
                ini.Set("Inventory", customDataKey, MyFixedPoint.Ceiling(item.Value).ToIntSafe());
            }
            return ini.ToString();
        }
    }
}