using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;

namespace InventorySorter.VirtualInventory
{
    public class InventoryInfo
    {
        private static readonly char[] SectionEndCharacters = { '\r', '\n', ']' };
        private static readonly MyIni IniParser = new MyIni();
        public byte Priority;
        public readonly TypeRequests TypeRequests;
        public List<RequestData> Requests;
        public RequestValidationStatus RequestStatus;
        public readonly Dictionary<MyDefinitionId, MyFixedPoint> VirtualInventory;
        public readonly Dictionary<MyDefinitionId, MyFixedPoint> LowBottleCount;
        public MyFixedPoint VirtualVolume;
        public MyFixedPoint VirtualMass;
        public readonly MyFixedPoint MaxVolume;
        public readonly MyFixedPoint MaxMass;
        public readonly MyInventoryConstraint Constraint;
        public readonly MyInventory RealInventory;
        public readonly IMyTerminalBlock Block;
        public readonly MyIniParseResult ConfigParseResult;
        public readonly bool SupportsConveyors;
        public bool IsSatisfied;

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
            IsSatisfied = false;

            // Generate constraint like Keen does since it's not an inventory constraint
            if (Constraint == null && Block is IMyConveyorSorter)
            {
                var sorter = Block as IMyConveyorSorter;
                Constraint = new MyInventoryConstraint(string.Empty, null, sorter.Mode == Sandbox.ModAPI.Ingame.MyConveyorSorterMode.Whitelist);
                var filterList = new List<Sandbox.ModAPI.Ingame.MyInventoryItemFilter>();
                sorter.GetFilterList(filterList);
                foreach (var filter in filterList)
                {
                    if (filter.AllSubTypes)
                    {
                        Constraint.AddObjectBuilderType(filter.ItemId.TypeId);
                    }
                    else
                    {
                        Constraint.Add(filter.ItemId);
                    }
                }
            }

            foreach (var item in realInventory.GetItems())
            {
                var itemId = item.Content.GetId();
                VirtualInventory[itemId] = VirtualInventory.GetValueOrDefault(itemId, MyFixedPoint.Zero) + item.Amount;

                var bottle = item.Content as MyObjectBuilder_GasContainerObject;
                if (bottle?.GasLevel < 1f)
                {
                    if (LowBottleCount == null)
                    {
                        LowBottleCount = new Dictionary<MyDefinitionId, MyFixedPoint>();
                    }

                    LowBottleCount[itemId] = LowBottleCount.GetValueOrDefault(itemId, MyFixedPoint.Zero) + item.Amount;
                }
            }

            var config = CargoSorterSessionComponent.Instance?.Config;
            if (config == null || Block == null)
            {
                TypeRequests = TypeRequests.Nothing;
                return;
            }

            if (Block.CustomName.InsensitiveContains(config.SpecialContainerKeyword))
            {
                TypeRequests = TypeRequests.Special;
                ConfigParseResult = ParseCustomDataRequests("Inventory", sectionName != string.Empty);
            }
            else
            {
                if (Block.CustomName.InsensitiveContains(config.AnyContainerKeyword))
                {
                    TypeRequests = TypeRequests.Ores | TypeRequests.Ingots | TypeRequests.Components | TypeRequests.Tools | TypeRequests.Ammo | TypeRequests.Bottles | TypeRequests.Consumables | TypeRequests.Ingredients;
                }
                else
                {
                    if (Block.CustomName.InsensitiveContains(config.OreContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ores;
                    }

                    if (Block.CustomName.InsensitiveContains(config.IngotContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ingots;
                    }

                    if (Block.CustomName.InsensitiveContains(config.ComponentContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Components;
                    }

                    if (Block.CustomName.InsensitiveContains(config.ToolContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Tools;
                    }

                    if (Block.CustomName.InsensitiveContains(config.AmmoContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ammo;
                    }

                    if (Block.CustomName.InsensitiveContains(config.BottleContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Bottles;
                    }

                    if (Block.CustomName.InsensitiveContains(config.ConsumablesContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Consumables;
                    }

                    if (Block.CustomName.InsensitiveContains(config.IngredientsContainerKeyword))
                    {
                        TypeRequests |= TypeRequests.Ingredients;
                    }
                }

                if (Block.CustomName.InsensitiveContains(config.LimitedContainerKeyword))
                {
                    TypeRequests |= TypeRequests.Limited;
                    ConfigParseResult = ParseCustomDataRequests("Inventory", sectionName != string.Empty);
                }
            }

            if (sectionName != string.Empty && Block.CustomData.InsensitiveContains(sectionName))
            {
                TypeRequests = TypeRequests.Special;
                ConfigParseResult = ParseCustomDataRequests(sectionName);
            }

            // if (Requests != null && Requests.Count > 0)
            // {
            //     foreach (var request in Requests)
            //     {
            //         MyLog.Default.WriteLineAndConsole($"CargoSort ({Block.CustomName}): {request.DefinitionId} {request.Amount} {request.Flag}");
            //     }
            // }

            if ((TypeRequests == TypeRequests.Special || TypeRequests == TypeRequests.Limited) && Requests != null && Requests.Count > 0)
            {
                if (!ComputeAndValidateRequests(Requests, realInventory))
                {
                    RequestStatus |= RequestValidationStatus.TooMuchVolume;
                }

                IsSatisfied = true;
                foreach (var request in Requests)
                {
                    if (request.Flag == RequestFlags.All)
                    {
                        IsSatisfied = false;
                        continue;
                    }

                    var currentAmount = VirtualInventory.GetValueOrDefault(request.DefinitionId);
                    if (request.Flag == RequestFlags.None || request.Flag == RequestFlags.Max || request.Flag == RequestFlags.Percent)
                    {
                        if (currentAmount == request.Amount)
                        {
                            continue;
                        }

                        IsSatisfied = false;
                        break;
                    }

                    if (request.Flag == RequestFlags.Limit)
                    {
                        if (currentAmount <= request.Amount)
                        {
                            continue;
                        }

                        IsSatisfied = false;
                        break;
                    }

                    if (request.Flag == RequestFlags.Minimum)
                    {
                        if (currentAmount >= request.Amount)
                        {
                            continue;
                        }

                        IsSatisfied = false;
                        break;
                    }
                }
            }

            var priorityStartIndex = Block.CustomName.IndexOf("[P", StringComparison.OrdinalIgnoreCase);
            if (priorityStartIndex > -1)
            {
                priorityStartIndex += 2;
                var priorityLen = 0;
                var foundTerminator = false;
                while (priorityStartIndex + priorityLen < Block.CustomName.Length && priorityLen < 4)
                {
                    if (Block.CustomName[priorityStartIndex + priorityLen] == ']')
                    {
                        foundTerminator = true;
                        break;
                    }

                    priorityLen++;
                }

                if (priorityLen > 0 && foundTerminator)
                {
                    if (!byte.TryParse(Block.CustomName.Substring(priorityStartIndex, priorityLen), out Priority))
                    {
                        Priority = byte.MaxValue;
                    }
                }
            }

            if (!TypeRequests.Equals(TypeRequests.Nothing) || Priority != byte.MaxValue)
            {
                return;
            }

            // Handle blocks that have special requirements that aren't otherwise specified as needing them
            if (Block is IMyGasGenerator)
            {
                TypeRequests = TypeRequests.GasGeneratorOre;
                Priority = 0;
                var generator = (IMyGasGenerator)Block;
                if (Block.IsWorking && generator.AutoRefill)
                {
                    if (generator.IsProducing)
                    {
                        TypeRequests |= TypeRequests.GasBottles;
                    }
                    else
                    {
                        foreach (var item in VirtualInventory)
                        {
                            if (item.Key.TypeId == typeof(MyObjectBuilder_OxygenContainerObject) ||
                                item.Key.TypeId == typeof(MyObjectBuilder_GasContainerObject) ||
                                item.Value == MyFixedPoint.Zero)
                            {
                                continue;
                            }

                            TypeRequests |= TypeRequests.GasBottles;
                            break;
                        }
                    }
                }
            }
            else if (Block is IMyAssembler) // Survival kits are OK here too
            {
                TypeRequests = TypeRequests.AssemblerIngots;
                Priority = 0;

                if (!Block.CustomData.Contains("[Inventory]"))
                {
                    return;
                }

                ConfigParseResult = ParseCustomDataRequests("Inventory");
                if (ConfigParseResult.Success)
                {
                    TypeRequests |= TypeRequests.Limited;
                }
            }
            else if (Block is IMyRefinery)
            {
                TypeRequests = TypeRequests.RefineryOre;
                if (((IMyRefinery)Block).UseConveyorSystem)
                {
                    IsSatisfied = true;
                }

                Priority = 0;
            }
            else if (Block is IMyGasTank)
            {
                var gasTank = (IMyGasTank)Block;
                if (Block.IsWorking && gasTank.AutoRefillBottles && gasTank.FilledRatio != 0)
                {
                    TypeRequests = TypeRequests.GasBottles;
                    IsSatisfied = false;
                    Priority = 0;
                }
                else
                {
                    IsSatisfied = true;
                }
            }
            else if (Block is IMyReactor)
            {
                TypeRequests = TypeRequests.ReactorFuel;
                var reactor = Block as IMyReactor;
                if (reactor?.UseConveyorSystem == false && Block.CustomData.Contains("[Inventory]"))
                {
                    ConfigParseResult = ParseCustomDataRequests("Inventory");
                    if (ConfigParseResult.Success && Requests.Count > 0)
                    {
                        TypeRequests = TypeRequests.Special;
                    }
                }

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
                if (((IMyConveyorSorter)Block).DrainAll)
                {
                    IsSatisfied = true;
                }
                else if (Block.CustomData.Contains("[Inventory]"))
                {
                    ConfigParseResult = ParseCustomDataRequests("Inventory");
                    if (ConfigParseResult.Success && Requests.Count > 0)
                    {
                        TypeRequests |= TypeRequests.Limited;
                    }
                }
            }
            //MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.CustomName} wants {TypeRequests} with priority {Priority}");
        }

        private bool ComputeAndValidateRequests(List<RequestData> requests, MyInventory realInventory)
        {
            if (requests.Count > realInventory.MaxItemCount)
            {
                return false;
            }

            var sumVolume = MyFixedPoint.Zero;
            var sumMass = MyFixedPoint.Zero;

            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                if (realInventory.Constraint != null && !realInventory.Constraint.Check(request.DefinitionId))
                {
                    return false;
                }

                float volume;
                float mass;
                bool hasIntegralAmounts;
                if (CargoSorterSessionComponent.TryGetPhysicalItemProperties(request.DefinitionId, out volume, out mass, out hasIntegralAmounts))
                {
                    var amount = request.Amount;

                    if (request.Flag == RequestFlags.All)
                    {
                        amount = hasIntegralAmounts ? 1 : MyFixedPoint.SmallestPossibleValue;
                    }
                    else if (request.Flag == RequestFlags.Max)
                    {
                        amount = ComputeAmountThatCouldFit(volume, mass, hasIntegralAmounts, sumVolume, sumMass);
                        if (amount == MyFixedPoint.Zero)
                        {
                            return false;
                        }

                        request.Amount = amount;
                        requests[index] = request;
                    }
                    else if (request.Flag == RequestFlags.Percent)
                    {
                        amount = (MyFixedPoint)((double)ComputeAmountThatCouldFit(volume, mass, hasIntegralAmounts) * ((double)request.Amount / 100.0));
                        if (hasIntegralAmounts)
                        {
                            amount = MyFixedPoint.Floor(amount);
                        }

                        if (amount == MyFixedPoint.Zero)
                        {
                            return false;
                        }

                        request.Amount = amount;
                        requests[index] = request;
                    }
                    else
                    {
                        if (hasIntegralAmounts)
                        {
                            amount = MyFixedPoint.Floor((MyFixedPoint)(Math.Round((double)amount * 1000.0) / 1000.0));
                        }
                    }

                    sumVolume += amount * volume;
                    sumMass += amount * mass;
                }

                if (sumVolume > realInventory.MaxVolume || sumMass > realInventory.MaxMass)
                {
                    return false;
                }
            }

            return sumVolume <= realInventory.MaxVolume && sumMass <= realInventory.MaxMass;
        }

        private MyIniParseResult ParseCustomDataRequests(string sectionName, bool skipCreate = false)
        {
            var quotaParseResult = new MyIniParseResult();
            if (!Util.IsValid(Block))
            {
                // MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.CustomName} isn't a terminal block");
                return quotaParseResult;
            }

            IniParser.Clear();
            if (!skipCreate && IsCustomDataEmpty(Block.CustomData))
            {
                Block.CustomData = BuildCurrentContentsSpecialData(Block, sectionName, IniParser);
            }
            else if (!IniParser.TryParse(Block.CustomData, out quotaParseResult))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.CustomName} failed to parse customdata into Special config");
                RequestStatus |= RequestValidationStatus.InvalidCustomData;
                return quotaParseResult;
            }

            if (!skipCreate && !IniParser.ContainsSection(sectionName))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.CustomName} has no {sectionName} config section");
                Block.CustomData = BuildCurrentContentsSpecialData(Block, sectionName, IniParser);
            }

            var iniKeys = new List<MyIniKey>();
            IniParser.GetKeys(sectionName, iniKeys);
            var priorRequests = Requests?.Count > 0 ? Requests : null;
            if (Requests == null || priorRequests != null)
            {
                Requests = new List<RequestData>(iniKeys.Count);
            }

            var specificIndex = -1;
            //MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.CustomName} has {iniKeys.Count}");
            foreach (var iniKey in iniKeys)
            {
                if (iniKey.IsEmpty)
                {
                    continue;
                }

                // Allow forcing a new priority with a special key
                if (iniKey.Name.Equals("Priority", StringComparison.OrdinalIgnoreCase))
                {
                    var newPriority = IniParser.Get(iniKey).ToByte(byte.MaxValue);
                    Priority = newPriority;
                    continue;
                }

                MyDefinitionId definitionId;
                if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                {
                    RequestStatus |= RequestValidationStatus.InvalidItem;
                    continue;
                }

                // Check constraints
                if (Constraint != null && !Constraint.Check(definitionId))
                {
                    RequestStatus |= RequestValidationStatus.InvalidItem;
                    continue;
                }

                var value = IniParser.Get(iniKey);
                // MyLog.Default.WriteLineAndConsole($"CargoSort: {Block.CustomName} key {iniKey.Name} {value}");
                var valueString = value.ToString();
                if (string.IsNullOrWhiteSpace(valueString))
                {
                    specificIndex++;
                    Requests.AddOrInsert(new RequestData(definitionId, 0, RequestFlags.None), specificIndex);
                }
                else
                {
                    if (valueString.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        Requests.Add(new RequestData(definitionId, ComputeAmountThatCouldFit(definitionId), RequestFlags.All));
                        continue;
                    }

                    if (valueString.Equals("Max", StringComparison.OrdinalIgnoreCase))
                    {
                        Requests.Add(new RequestData(definitionId, 0, RequestFlags.Max));
                        continue;
                    }

                    int itemCount;
                    if (!int.TryParse(valueString.TrimEnd('%', 'l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                    {
                        RequestStatus |= RequestValidationStatus.InvalidCount;
                        continue;
                    }

                    var requestValue = new RequestData(definitionId, itemCount, RequestFlags.None);

                    var lastChar = valueString[valueString.Length - 1];
                    if (lastChar == 'L' || lastChar == 'l')
                    {
                        requestValue.Flag = RequestFlags.Limit;
                    }
                    else if (lastChar == 'M' || lastChar == 'm')
                    {
                        requestValue.Flag = RequestFlags.Minimum;
                    }
                    else if (lastChar == '%')
                    {
                        if (itemCount > 100)
                        {
                            RequestStatus |= RequestValidationStatus.InvalidCount;
                            continue;
                        }

                        requestValue.Flag = RequestFlags.Percent;
                    }

                    specificIndex++;
                    Requests.AddOrInsert(requestValue, specificIndex);
                }
            }

            IniParser.Clear();
            if (priorRequests == null)
            {
                return quotaParseResult;
            }

            foreach (var priorRequest in priorRequests)
            {
                var existingIndex = Requests.FindIndex(r => r.DefinitionId == priorRequest.DefinitionId);
                if (existingIndex >= 0)
                {
                    continue;
                }

                if (priorRequest.Flag == RequestFlags.All || priorRequest.Flag == RequestFlags.Max)
                {
                    Requests.Add(priorRequest);
                }
                else
                {
                    specificIndex++;
                    Requests.AddOrInsert(priorRequest, specificIndex);
                }
            }

            return quotaParseResult;
        }

        private bool IsCustomDataEmpty(string customData) { return string.IsNullOrWhiteSpace(customData) || customData.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase) || customData.Equals(bool.FalseString, StringComparison.OrdinalIgnoreCase); }

        private string BuildCurrentContentsSpecialData(IMyCubeBlock block, string sectionName, MyIni ini)
        {
            var items = new Dictionary<MyDefinitionId, MyFixedPoint>();
            for (var i = 0; i < block.InventoryCount; i++)
            {
                var inv = (MyInventory)block.GetInventory(i);
                foreach (var item in inv.GetItems())
                {
                    var id = item.Content.GetId();
                    MyFixedPoint amount;
                    items.TryGetValue(id, out amount);
                    amount += item.Amount;
                    items[id] = amount;
                }
            }

            return items.Count == 0 ? string.Empty : BuildCustomData(items, false, sectionName, ini);
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
                amount = MyFixedPoint.Floor(amount);
            }

            volumeToBeMoved = amount * volume;
            massToBeMoved = amount * mass;
            return (volumeToBeMoved + VirtualVolume <= MaxVolume || (MaxVolume - VirtualVolume - volumeToBeMoved).Abs().RawValue < 100) && (massToBeMoved + VirtualMass <= MaxMass || (MaxMass - VirtualMass - massToBeMoved).Abs().RawValue < 100);
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

            var a = MyFixedPoint.Max((MyFixedPoint)((double)(MaxVolume - VirtualVolume) / (double)volume), 0);
            var b = MyFixedPoint.Max((MyFixedPoint)((double)(MaxMass - VirtualMass) / (double)mass), 0);
            var amount = MyFixedPoint.Min(a, b);
            if (hasIntegralAmounts || forceIntegralAmount)
            {
                amount = MyFixedPoint.Floor(amount);
            }

            return amount;
        }

        internal MyFixedPoint ComputeAmountThatCouldFit(MyDefinitionId contentId, bool forceIntegralAmount = false, MyFixedPoint volumeReserved = default(MyFixedPoint), MyFixedPoint massReserved = default(MyFixedPoint))
        {
            float mass;
            float volume;
            bool hasIntegralAmounts;
            if (!CargoSorterSessionComponent.TryGetPhysicalItemProperties(contentId, out volume, out mass, out hasIntegralAmounts))
            {
                return MyFixedPoint.Zero;
            }

            return ComputeAmountThatCouldFit(volume, mass, hasIntegralAmounts, volumeReserved, massReserved);
        }

        private MyFixedPoint ComputeAmountThatCouldFit(float volume, float mass, bool hasIntegralAmounts, MyFixedPoint volumeReserved = default(MyFixedPoint), MyFixedPoint massReserved = default(MyFixedPoint))
        {
            var a = MyFixedPoint.Max((MyFixedPoint)((double)(MaxVolume - volumeReserved) / (double)volume), 0);
            var b = MyFixedPoint.Max((MyFixedPoint)((double)(MaxMass - massReserved) / (double)mass), 0);
            var amount = MyFixedPoint.Min(a, b);
            if (hasIntegralAmounts)
            {
                amount = MyFixedPoint.Floor(amount);
            }

            return amount;
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

            if (ini.ContainsSection(sectionName))
            {
                ini.DeleteSection(sectionName);
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