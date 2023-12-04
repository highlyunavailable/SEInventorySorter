using System;
using System.Collections.Generic;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace CargoSorter
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class CargoSorterSessionComponent : MySessionComponentBase
    {
        public static CargoSorterSessionComponent Instance { get; private set; }
        public CargoSorterConfiguration Config { get; private set; }

        private readonly HashSet<MyDefinitionId> allOres = new HashSet<MyDefinitionId>();
        private readonly HashSet<MyDefinitionId> allIngots = new HashSet<MyDefinitionId>();
        private readonly HashSet<MyDefinitionId> allComponents = new HashSet<MyDefinitionId>();
        private readonly HashSet<MyDefinitionId> allAmmo = new HashSet<MyDefinitionId>();
        private readonly HashSet<MyDefinitionId> allTools = new HashSet<MyDefinitionId>();
        private readonly HashSet<MyDefinitionId> allBottles = new HashSet<MyDefinitionId>();

        private readonly Dictionary<string, MyDefinitionId> stringPhysicalItemMap = new Dictionary<string, MyDefinitionId>(StringComparer.InvariantCultureIgnoreCase);

        public override void LoadData()
        {
            if (Util.IsDedicatedServer)
            {
                return;
            }

            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;

            Config = CargoSorterConfiguration.LoadSettings();

            Instance = this;

            foreach (var definition in MyDefinitionManager.Static.GetPhysicalItemDefinitions())
            {
                if (!definition.Enabled || !definition.Public)
                {
                    continue;
                }
                MakeNormalizedId(definition);
                if (definition.IsOre)
                {
                    allOres.Add(definition.Id);
                }
                if (definition.IsIngot)
                {
                    allIngots.Add(definition.Id);
                }
                if (definition is MyToolItemDefinition || definition is MyWeaponItemDefinition || definition is MyUsableItemDefinition || definition is MyDatapadDefinition)
                {
                    allTools.Add(definition.Id);
                }
                if (definition is MyOxygenContainerDefinition)
                {
                    allBottles.Add(definition.Id);
                }
                if (definition is MyComponentDefinition)
                {
                    allComponents.Add(definition.Id);
                }
                if (definition is MyAmmoMagazineDefinition)
                {
                    allAmmo.Add(definition.Id);
                }
            }
            foreach (var definition in MyDefinitionManager.Static.GetHandItemDefinitions())
            {
                if (!definition.Enabled || !definition.Public)
                {
                    continue;
                }
                MakeNormalizedId(definition);
                allTools.Add(definition.PhysicalItemId);
            }
        }

        private void MakeNormalizedId(MyDefinitionBase definition)
        {
            var normalizedStringId = definition.Id.ToString().Replace(MyObjectBuilderType.LEGACY_TYPE_PREFIX, string.Empty, StringComparison.InvariantCultureIgnoreCase).ToLowerInvariant();
            stringPhysicalItemMap[normalizedStringId] = definition.Id;
        }

        public bool TryGetNormalizedItemDefinition(string shortStringName, out MyDefinitionId definitionId)
        {
            if (stringPhysicalItemMap.TryGetValue(shortStringName, out definitionId))
            {
                return true;
            }

            return false;
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (!messageText.StartsWith("/sort", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            sendToOthers = false;
            var shipController = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyShipController;
            if (shipController == null)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", "You must be seated on a grid to sort!");
                return;
            }

            long localPlayerId = MyAPIGateway.Session.LocalHumanPlayer.IdentityId;
            var workData = new CargoSorterWorkData(shipController.CubeGrid);
            MyAPIGateway.Parallel.Start(SortInventoryAction, SortInventoryCallback, workData);
        }

        private void SortInventoryAction(WorkData data)
        {
            try
            {
                var workData = (CargoSorterWorkData)data;
                List<IMyCubeGrid> excludedGrids = new List<IMyCubeGrid>();

                var tree = new GridConnectorTree(workData.RootGrid);

                var nodes = tree.GatherRecursive(c =>
                {
                    return c.DisplayNameText?.Contains("[nosort]") == false &&
                    c.OtherConnector?.CubeGrid?.CustomName?.Contains("[nosort]") == false;
                });

                foreach (var cubeGrid in GridConnectorTree.GatherGrids(nodes))
                {
                    GatherInventory(cubeGrid.GetFatBlocks<IMyTerminalBlock>(), workData);
                }

                workData.Inventories.SortNoAlloc((InventoryInfo x, InventoryInfo y) =>
                {
                    // Blocks and specials go first
                    if (Util.IsSpecial(x.TypeRequests) && !Util.IsSpecial(y.TypeRequests))
                    {
                        return -1;
                    }
                    else if (!Util.IsSpecial(x.TypeRequests) && Util.IsSpecial(y.TypeRequests))
                    {
                        return 1;
                    }

                    // Priority applies next
                    var comparison = x.Priority.CompareTo(y.Priority);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    // Sort by name so that the first items in the terminal tend to fill up first
                    comparison = string.Compare(x.Block.DisplayNameText, y.Block.DisplayNameText);
                    if (comparison != 0)
                    {
                        comparison = string.Compare(x.Block.DisplayNameText, y.Block.DisplayNameText);
                        return comparison;
                    }

                    // Use the entity ID as a fallback so it's fairly stable ordering
                    return x.Block.EntityId.CompareTo(y.Block.EntityId);
                });

                //foreach (var item in workData.Inventories)
                //{
                //    MyLog.Default.WriteLineAndConsole($"CargoSort: {item.Block.DisplayNameText}");
                //}

                BuildExcessItemMovement(workData);
                BuildDesiredItemMovement(workData);

                //MyLog.Default.WriteLineAndConsole($"CargoSort: Movement Data {workData.MovementData.Count} ops:\n{string.Join("\n", workData.MovementData)}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"CargoSort: Sort failed with exception:\n{ex}");
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Internal error: {ex.Message}");
            }
        }

        private void GatherInventory(IEnumerable<IMyTerminalBlock> blocks, CargoSorterWorkData workData)
        {
            foreach (var block in blocks)
            {
                if (!Util.IsValid(block) || block.InventoryCount == 0 || !block.HasLocalPlayerAccess() || IsIgnored(block))
                {
                    continue;
                }
                for (int i = 0; i < block.InventoryCount; i++)
                {
                    var inventory = block.GetInventory(i) as MyInventory;
                    var inventoryInfo = new InventoryInfo(inventory);
                    workData.Inventories.Add(inventoryInfo);
                    foreach (var item in inventoryInfo.VirtualInventory)
                    {
                        MyFixedPoint amount;
                        if (workData.AvailableForDistribution.TryGetValue(item.Key, out amount))
                        {
                            amount += item.Value;
                        }
                        else
                        {
                            amount = item.Value;
                        }
                        workData.AvailableForDistribution[item.Key] = amount;
                    }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Special))
                    {
                        foreach (var request in inventoryInfo.Requests)
                        {
                            MyFixedPoint amount;
                            if (workData.AvailableForDistribution.TryGetValue(request.Key, out amount))
                            {
                                amount -= request.Value;
                            }
                            else
                            {
                                amount = -request.Value;
                            }
                            workData.AvailableForDistribution[request.Key] = amount;
                        }
                    }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.ReactorFuel))
                    {
                        var reactor = block as IMyReactor;
                        if (reactor == null)
                        {
                            continue;
                        }
                        var def = MyDefinitionManager.Static.GetDefinition(block.BlockDefinition) as MyReactorDefinition;
                        if (def == null || def.FuelInfos == null || def.FuelInfos.Length == 0)
                        {
                            continue;
                        }

                        foreach (var fuelInfo in def.FuelInfos)
                        {
                            int amount;
                            var key = new ValueTuple<TypeRequests, MyDefinitionId>(TypeRequests.ReactorFuel, fuelInfo.FuelId);
                            if (workData.RequestTypeCount.TryGetValue(key, out amount))
                            {
                                amount++;
                            }
                            else
                            {
                                amount = 1;
                            }
                            workData.RequestTypeCount[key] = amount;
                        }
                    }
                }
            }
        }
        private bool IsIgnored(IMyTerminalBlock block)
        {
            foreach (var item in Instance.Config.LockedContainerKeywords)
            {
                if (block.DisplayNameText.Contains(item))
                {
                    return true;
                }
            }
            return false;
        }
        private void BuildExcessItemMovement(CargoSorterWorkData workData)
        {
            //MyLog.Default.WriteLineAndConsole($"CargoSort: Removing excess items");
            List<MyDefinitionId> inventoryKeys = new List<MyDefinitionId>();
            for (int sourceInvIndex = 0; sourceInvIndex < workData.Inventories.Count; sourceInvIndex++)
            {
                var sourceInventory = workData.Inventories[sourceInvIndex];
                var sourcePBInv = (VRage.Game.ModAPI.Ingame.IMyInventory)sourceInventory.RealInventory;
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv source: {sourceInventory.Block?.DisplayNameText}");
                if (sourceInventory.VirtualInventory.Count == 0) // Nothing to transfer out
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping due to no items");
                    continue;
                }

                inventoryKeys.Clear();
                inventoryKeys.AddRange(sourceInventory.VirtualInventory.Keys);
                for (int destInvIndex = 0; destInvIndex < workData.Inventories.Count; destInvIndex++)
                {
                    var destInventory = workData.Inventories[destInvIndex];
                    if (destInventory.TypeRequests.Equals(TypeRequests.Nothing) || destInvIndex == sourceInvIndex)
                    {
                        continue;
                    }

                    var destPBInv = (VRage.Game.ModAPI.Ingame.IMyInventory)destInventory.RealInventory;
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv destination: {destInventory.Block?.DisplayNameText}");

                    for (int invKeyIndex = inventoryKeys.Count - 1; invKeyIndex >= 0; invKeyIndex--)
                    {
                        var virtualItemKey = inventoryKeys[invKeyIndex];
                        MyFixedPoint virtualItemValue;
                        if (!sourceInventory.VirtualInventory.TryGetValue(virtualItemKey, out virtualItemValue) || virtualItemValue <= MyFixedPoint.Zero)
                        {
                            inventoryKeys.RemoveAtFast(invKeyIndex);
                            continue;
                        }
                        var sourceAmountExcess = -CalculateAmountWanted(sourceInventory, virtualItemKey, virtualItemValue, workData);
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: Excess: {sourceAmountExcess}");
                        if (sourceAmountExcess > MyFixedPoint.Zero)
                        {
                            MyFixedPoint amountToBeMoved = MyFixedPoint.Min(CalculateAmountWanted(destInventory, virtualItemKey, virtualItemValue, workData), sourceAmountExcess);
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: amountToBeMoved {virtualItemKey}: {amountToBeMoved}");
                            if (amountToBeMoved <= MyFixedPoint.Zero || !sourcePBInv.CanTransferItemTo(destPBInv, virtualItemKey))
                            {
                                continue;
                            }

                            MyFixedPoint volumeToBeMoved;
                            MyFixedPoint massToBeMoved;
                            if (!destInventory.CanItemsBeAdded(amountToBeMoved, virtualItemKey, out volumeToBeMoved, out massToBeMoved))
                            {
                                //MyLog.Default.WriteLineAndConsole($"CargoSort: Could not add {virtualItemKey} with amount {amountToBeMoved} to inventory");
                                continue;
                            }
                            AppendInventoryOperation(workData, new InventoryMovement(sourceInventory, destInventory, virtualItemKey, amountToBeMoved, volumeToBeMoved, massToBeMoved));
                        }
                    }
                }
            }
        }
        private void BuildDesiredItemMovement(CargoSorterWorkData workData)
        {
            //MyLog.Default.WriteLineAndConsole($"CargoSort: Moving desired items");
            List<MyDefinitionId> inventoryKeys = new List<MyDefinitionId>();
            for (int sourceInvIndex = workData.Inventories.Count - 1; sourceInvIndex >= 0; sourceInvIndex--)
            {
                var sourceInventory = workData.Inventories[sourceInvIndex];
                var sourcePBInv = (VRage.Game.ModAPI.Ingame.IMyInventory)sourceInventory.RealInventory;
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv source: {sourceInventory.Block?.DisplayNameText}");
                if (sourceInventory.VirtualInventory.Count == 0) // Nothing to transfer out
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping due to no items");
                    continue;
                }

                inventoryKeys.Clear();
                inventoryKeys.AddRange(sourceInventory.VirtualInventory.Keys);
                for (int destInvIndex = 0; destInvIndex < workData.Inventories.Count; destInvIndex++)
                {
                    if (sourceInventory.VirtualInventory.Count == 0)
                    {
                        break;
                    }

                    var destInventory = workData.Inventories[destInvIndex];
                    if (destInventory.TypeRequests.Equals(TypeRequests.Nothing) || destInvIndex == sourceInvIndex)
                    {
                        continue;
                    }

                    var sourceSpecial = Util.IsSpecial(sourceInventory.TypeRequests);
                    var destSpecial = Util.IsSpecial(destInventory.TypeRequests);

                    if (sourceSpecial && (!Config.AllowSpecialSteal || !destSpecial))
                    {
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv destination skipped due to not being special: {destInventory.Block?.DisplayNameText}");
                        continue;
                    }

                    // If they're both special or nonspecial then apply priority, otherwise we probably need to take
                    // from a higher priority normal container to satisfy a lower priority special container.
                    if (sourceSpecial == destSpecial && (sourceInventory.Priority <= destInventory.Priority))
                    {
                        continue;
                    }

                    var destPBInv = (VRage.Game.ModAPI.Ingame.IMyInventory)destInventory.RealInventory;
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv destination: {destInventory.Block?.DisplayNameText}");

                    for (int invKeyIndex = inventoryKeys.Count - 1; invKeyIndex >= 0; invKeyIndex--)
                    {
                        var virtualItemKey = inventoryKeys[invKeyIndex];
                        MyFixedPoint virtualItemValue;
                        if (!sourceInventory.VirtualInventory.TryGetValue(virtualItemKey, out virtualItemValue) || virtualItemValue <= MyFixedPoint.Zero)
                        {
                            inventoryKeys.RemoveAtFast(invKeyIndex);
                            continue;
                        }
                        MyFixedPoint amountToBeMoved = MyFixedPoint.Min(CalculateAmountWanted(destInventory, virtualItemKey, virtualItemValue, workData), virtualItemValue);
                        if (amountToBeMoved <= MyFixedPoint.Zero || !sourcePBInv.CanTransferItemTo(destPBInv, virtualItemKey))
                        {
                            continue;
                        }

                        //MyLog.Default.WriteLineAndConsole($"CargoSort: amountToBeMoved {virtualItemKey}: {amountToBeMoved}");

                        MyFixedPoint volumeToBeMoved;
                        MyFixedPoint massToBeMoved;
                        if (!destInventory.CanItemsBeAdded(amountToBeMoved, virtualItemKey, out volumeToBeMoved, out massToBeMoved))
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: Could not add {virtualItemKey} with amount {amountToBeMoved} to inventory");
                            continue;
                        }
                        AppendInventoryOperation(workData, new InventoryMovement(sourceInventory, destInventory, virtualItemKey, amountToBeMoved, volumeToBeMoved, massToBeMoved));
                    }
                }
            }
        }
        private MyFixedPoint CalculateAmountWanted(InventoryInfo inventoryInfo, MyDefinitionId definitionId, MyFixedPoint currentValue, CargoSorterWorkData workData)
        {
            if (inventoryInfo.Constraint != null && !inventoryInfo.Constraint.Check(definitionId))
            {
                return -currentValue;
            }
            MyFixedPoint virtualAmount;
            var percentFull = (float)inventoryInfo.VirtualVolume / (float)inventoryInfo.MaxVolume;
            switch (inventoryInfo.TypeRequests)
            {
                case TypeRequests.Nothing:
                    return -currentValue;
                case TypeRequests.GasGeneratorOre:
                    if (allBottles.Contains(definitionId))
                    {
                        return -currentValue;
                    }
                    inventoryInfo.VirtualInventory.TryGetValue(definitionId, out virtualAmount);
                    return percentFull < Config.GasGeneratorFillPercent / 2f || percentFull > 1f - ((1f - Config.GasGeneratorFillPercent) / 2f)
                        ? inventoryInfo.ComputeAmountThatCouldFit(definitionId, true,
                        (float)inventoryInfo.MaxVolume * (1f - Config.GasGeneratorFillPercent),
                        (float)inventoryInfo.MaxMass * (1f - Config.GasGeneratorFillPercent)) - virtualAmount
                        : MyFixedPoint.Zero;
                case TypeRequests.AssemblerIngots:
                    // If either inventory is too full, it doesn't matter which inventory it is - empty it.
                    // This can cause repulls but is better than halting all production because a refinery
                    // is pushing ingots it doesn't want into the assembler.
                    if (percentFull > 0.9)
                    {
                        return -currentValue;
                    }

                    // Make sure the output inventory is clear in normal operation.
                    var assembler = inventoryInfo.RealInventory?.Entity as IMyAssembler;
                    if (assembler != null)
                    {
                        MyInventoryConstraint constraintToCheck = null;
                        switch (assembler.Mode)
                        {
                            case Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly:
                                constraintToCheck = ((MyInventory)assembler.InputInventory)?.Constraint;
                                break;
                            case Sandbox.ModAPI.Ingame.MyAssemblerMode.Disassembly:
                                constraintToCheck = ((MyInventory)assembler.OutputInventory)?.Constraint;
                                break;
                        }

                        if (constraintToCheck != null && constraintToCheck.ConstrainedIds.Contains(definitionId) && percentFull < Config.EmptyAssemblerPercent)
                        {
                            return MyFixedPoint.Zero;
                        }
                    }
                    return -currentValue;
                case TypeRequests.RefineryOre:
                    var refinery = inventoryInfo.RealInventory?.Entity as IMyRefinery;
                    if (refinery != null)
                    {
                        var inputConstraint = ((MyInventory)refinery.InputInventory)?.Constraint;
                        if (inputConstraint != null && inputConstraint.ConstrainedIds.Contains(definitionId))
                        {
                            return MyFixedPoint.Zero;
                        }
                    }
                    return percentFull < Config.EmptyRefineryPercent ? MyFixedPoint.Zero : -currentValue;
                case TypeRequests.GasTankBottles:
                    return -currentValue;
                case TypeRequests.SorterItems:
                    var sorter = inventoryInfo.RealInventory?.Entity as IMyConveyorSorter;
                    return sorter != null && sorter.DrainAll ? MyFixedPoint.Zero : -currentValue;
                case TypeRequests.ReactorFuel:
                    var reactor = inventoryInfo.RealInventory?.Entity as IMyReactor;
                    if (reactor != null)
                    {
                        MyFixedPoint availableForDistribution;
                        if (!workData.AvailableForDistribution.TryGetValue(definitionId, out availableForDistribution) || availableForDistribution <= MyFixedPoint.Zero)
                        {
                            return MyFixedPoint.Zero;
                        }
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel {inventoryInfo.Block?.DisplayNameText} availableForDistribution {availableForDistribution}");
                        var typeKey = new ValueTuple<TypeRequests, MyDefinitionId>(TypeRequests.ReactorFuel, definitionId);
                        int typeRequestCount;
                        if (!workData.RequestTypeCount.TryGetValue(typeKey, out typeRequestCount) || availableForDistribution <= 0)
                        {
                            return MyFixedPoint.Zero;
                        }
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel {inventoryInfo.Block?.DisplayNameText} typeRequestCount {typeRequestCount}");

                        var expectedAmount = (MyFixedPoint)Math.Min(
                             (float)availableForDistribution / (float)typeRequestCount,
                             ((float)(reactor.CubeGrid?.GridSizeEnum == MyCubeSize.Large ? Config.ExpectedLargeGridReactorFuel : Config.ExpectedSmallGridReactorFuel) * reactor.PowerOutputMultiplier)
                            );
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel {inventoryInfo.Block?.DisplayNameText} expectedAmount {expectedAmount}");
                        inventoryInfo.VirtualInventory.TryGetValue(definitionId, out virtualAmount);

                        if (virtualAmount < expectedAmount * 0.5f)
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel too little, returning {expectedAmount - virtualAmount}");
                            return expectedAmount - virtualAmount;                            
                        }
                        else if (virtualAmount > expectedAmount)
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel too much, returning {expectedAmount - virtualAmount}");
                            return expectedAmount - virtualAmount;
                        }

                        //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel in range, returning 0 wanted");
                    }
                    return MyFixedPoint.Zero;
                case TypeRequests.WeaponAmmo:
                    return inventoryInfo.ComputeAmountThatFits(definitionId);
                case TypeRequests.Special:
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Special request amount {definitionId} {GetSpecialRequestAmount(inventoryInfo, definitionId, currentValue)}");
                    return GetSpecialRequestAmount(inventoryInfo, definitionId, currentValue);
                default:
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ores) && allOres.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ingots) && allIngots.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Components) && allComponents.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ammo) && allAmmo.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Tools) && allTools.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Bottles) && allBottles.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }

                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Limited))
                    {
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: Limited request amount {definitionId} {GetSpecialRequestAmount(inventoryInfo, definitionId, currentValue)}");
                        return GetSpecialRequestAmount(inventoryInfo, definitionId, currentValue);
                    }
                    return -currentValue;
            }
        }

        private static MyFixedPoint GetSpecialRequestAmount(InventoryInfo inventoryInfo, MyDefinitionId definitionId, MyFixedPoint currentValue)
        {
            MyFixedPoint desiredAmount;
            if (inventoryInfo.Requests.TryGetValue(definitionId, out desiredAmount))
            {
                bool limitedFlag = false;
                bool minimumFlag = false;

                if (desiredAmount < MyFixedPoint.MaxValue)
                {
                    limitedFlag = (desiredAmount.RawValue & InventoryInfo.FixedPointFlagSpecialLimited.RawValue) != 0;
                    minimumFlag = (desiredAmount.RawValue & InventoryInfo.FixedPointFlagSpecialMinimum.RawValue) != 0;

                    if (limitedFlag || minimumFlag) // Clear out the flags so calculations can be correct.
                    {
                        desiredAmount = MyFixedPoint.Floor(desiredAmount);
                    }
                }

                MyFixedPoint virtualAmount;
                inventoryInfo.VirtualInventory.TryGetValue(definitionId, out virtualAmount);

                if ((!limitedFlag && !minimumFlag) || (limitedFlag && virtualAmount > desiredAmount) || (minimumFlag && virtualAmount < desiredAmount))
                {
                    return MyFixedPoint.Min(inventoryInfo.ComputeAmountThatFits(definitionId, true), desiredAmount - virtualAmount);
                }
                return MyFixedPoint.Zero;
            }
            return -currentValue;
        }

        private static void AppendInventoryOperation(CargoSorterWorkData workData, InventoryMovement operation)
        {
            operation.Source.VirtualVolume -= operation.Volume;
            operation.Source.VirtualMass -= operation.Mass;
            var sourceChangedAmount = operation.Source.VirtualInventory[operation.Item] - operation.Amount;
            if (sourceChangedAmount <= MyFixedPoint.Zero)
            {
                operation.Source.VirtualInventory.Remove(operation.Item);
            }
            else
            {
                operation.Source.VirtualInventory[operation.Item] = sourceChangedAmount;
            }

            operation.Destination.VirtualVolume += operation.Volume;
            operation.Destination.VirtualMass += operation.Mass;
            MyFixedPoint destChangedAmount;
            operation.Destination.VirtualInventory.TryGetValue(operation.Item, out destChangedAmount);
            destChangedAmount += operation.Amount;
            operation.Destination.VirtualInventory[operation.Item] = destChangedAmount;
            workData.MovementData.Add(operation);
        }

        private void SortInventoryCallback(WorkData data)
        {
            var workData = (CargoSorterWorkData)data;

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Virtual Inventories after moves:");
            //foreach (var inventory in workData.Inventories)
            //{
            //    MyLog.Default.WriteLineAndConsole($"CargoSort: {(inventory.RealInventory?.Entity as IMyCubeBlock).DisplayNameText}: Virtual Volume {inventory.VirtualVolume}, Virtual Mass {inventory.VirtualMass}, Contents:");
            //    foreach (var item in inventory.VirtualInventory)
            //    {
            //        MyLog.Default.WriteLineAndConsole($"CargoSort: {(inventory.RealInventory?.Entity as IMyCubeBlock).DisplayNameText}: {item.Key} : {item.Value}");
            //    }
            //}

            var transferRequests = ExecuteMovementData(workData);
            if (Config.ShowProgressNotifications)
            {
                if (transferRequests == 0)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "No transfers needed.");
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"{transferRequests} transfers requested.");
                }
            }
        }
        private int ExecuteMovementData(CargoSorterWorkData workData)
        {
            int transferRequests = 0;
            List<KeyValuePair<uint, MyFixedPoint>> itemOps = new List<KeyValuePair<uint, MyFixedPoint>>();
            foreach (var movement in workData.MovementData)
            {
                if (!Util.IsValid(movement.Source.RealInventory?.Entity) || !Util.IsValid(movement.Destination.RealInventory?.Entity))
                {
                    continue;
                }

                var items = movement.Source.RealInventory.GetItems();
                var needToMove = movement.Amount;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var item = items[i];
                    if (item.Content.GetId() != movement.Item)
                    {
                        continue;
                    }
                    var toTransfer = MyFixedPoint.Min(item.Amount, needToMove);
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Movement from: {(movement.Source.RealInventory?.Entity as IMyCubeBlock).DisplayNameText} ({movement.Source.TypeRequests}, P{movement.Source.Priority}) To: {(movement.Destination.RealInventory?.Entity as IMyCubeBlock).DisplayNameText} ({movement.Destination.TypeRequests}, P{movement.Destination.Priority}): {item.Content.TypeId}/{item.Content.SubtypeName} {toTransfer}");
                    MyInventory.TransferByUser(movement.Source.RealInventory, movement.Destination.RealInventory, item.ItemId, amount: toTransfer);
                    transferRequests++;
                    needToMove -= toTransfer;
                    if (needToMove <= MyFixedPoint.Zero)
                    {
                        break;
                    }
                }
            }
            return transferRequests;
        }

        protected override void UnloadData()
        {
            if (Util.IsDedicatedServer)
            {
                return;
            }
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            Instance = null;
        }
    }
}