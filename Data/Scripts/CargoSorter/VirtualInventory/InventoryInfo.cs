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

namespace CargoSorter
{
    public class InventoryInfo
    {
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

            if (Block.DisplayNameText.InsensitiveContains(config.SpecialContainerKeyword))
            {
                TypeRequests = TypeRequests.Special;
                Requests = new Dictionary<MyDefinitionId, RequestData>();
                ParseCustomDataRequests(this, Requests);
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
                if (Block.DisplayNameText.InsensitiveContains(config.LimitedContainerKeyword))
                {
                    TypeRequests |= TypeRequests.Limited;
                    Requests = new Dictionary<MyDefinitionId, RequestData>();
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

                    var assemberBlock = Block as IMyTerminalBlock;
                    if (Util.IsValid(assemberBlock))
                    {
                        if (assemberBlock.CustomData.Contains("[Inventory]"))
                        {
                            Requests = new Dictionary<MyDefinitionId, RequestData>();
                            ParseCustomDataRequests(this, Requests);
                        }
                    }
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
            }

            // Always mark sorters as having SorterItems request types
            if (Block is IMyConveyorSorter)
            {
                TypeRequests |= CargoSorterSessionComponent.Instance.IsSorter(Block as IMyConveyorSorter) ?
                    TypeRequests.SorterItems :
                    TypeRequests.ConsumableAmmo;

                if (Priority == byte.MaxValue) { Priority = 0; }
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

        private void ParseCustomDataRequests(InventoryInfo inventoryInfo, Dictionary<MyDefinitionId, RequestData> specialRequests)
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
                    specialRequests[definitionId] = new RequestData(0, RequestFlags.None);
                }
                else
                {
                    if (valueString.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        specialRequests[definitionId] = new RequestData(ComputeAmountThatCouldFit(definitionId), RequestFlags.All);
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
                    specialRequests[definitionId] = requestValue;
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

            return BuildCustomData(items, false, ini);
        }

        internal bool CanItemsFit(MyFixedPoint amount, MyDefinitionId itemDefinition, out MyFixedPoint volumeToBeMoved, out MyFixedPoint massToBeMoved)
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
            return !(volumeToBeMoved + VirtualVolume > MaxVolume) && !(massToBeMoved + VirtualMass > MaxMass);
        }

        internal MyFixedPoint ComputeAmountThatFits(MyDefinitionId contentId, bool forceIntegralAmount = false)
        {
            MyPhysicalItemDefinition physItem;
            if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(contentId, out physItem))
            {
                return MyFixedPoint.Zero;
            }
            MyFixedPoint a = MyFixedPoint.Max((MyFixedPoint)(((double)MaxVolume - (double)VirtualVolume) / (double)physItem.Volume), 0);
            MyFixedPoint b = MyFixedPoint.Max((MyFixedPoint)(((double)MaxMass - (double)VirtualMass) / (double)physItem.Mass), 0);
            MyFixedPoint amount = MyFixedPoint.Min(a, b);
            if (physItem.HasIntegralAmounts || forceIntegralAmount)
            {
                amount = MyFixedPoint.Floor((MyFixedPoint)(Math.Round((double)amount * 1000.0) / 1000.0));
            }
            return amount;
        }

        internal MyFixedPoint ComputeAmountThatCouldFit(MyDefinitionId contentId, bool forceIntegralAmount = false, float volumeReserved = 0f, float massReserved = 0f)
        {
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

        internal static string BuildCustomData(Dictionary<MyDefinitionId, MyFixedPoint> items, bool ceiling, MyIni ini = null)
        {
            if (ini == null)
            {
                ini = new MyIni();
            }
            ini.AddSection("Inventory");
            foreach (var item in items
                .Select(i => new KeyValuePair<string, int>(
                    CargoSorterSessionComponent.Instance.GetFriendlyDefinitionName(i.Key),
                    (ceiling ? MyFixedPoint.Ceiling(i.Value) : i.Value).ToIntSafe()))
                .OrderBy(i => i.Key))
            {
                ini.Set("Inventory", item.Key, item.Value);
            }
            return ini.ToString();
        }
    }
}