using System;
using System.Collections.Generic;
using System.Linq;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
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
                if (!definition.AvailableInSurvival)
                {
                    continue;
                }
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
                if (!definition.AvailableInSurvival)
                {
                    continue;
                }
                allTools.Add(definition.PhysicalItemId);
            }
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.StartsWith("/clownsort"))
            {
                sendToOthers = false;
                var shipController = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyShipController;
                if (shipController == null)
                {
                    return;
                }

                long localPlayerId = MyAPIGateway.Session.LocalHumanPlayer.IdentityId;
                var workData = new CargoSorterWorkData();
                shipController.CubeGrid.GetGridGroup(GridLinkTypeEnum.Logical).GetGrids(workData.Grids);

                var inventorySortTask = MyAPIGateway.Parallel.Start(SortInventoryAction, SortInventoryCallback, workData);

                return;
            }
        }

        private void SortInventoryAction(WorkData data)
        {
            try
            {
                var workData = (CargoSorterWorkData)data;
                List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                foreach (var cubeGrid in workData.Grids)
                {
                    if (!Util.IsValid(cubeGrid))
                    {
                        continue;
                    }
                    blocks.Clear();
                    blocks.EnsureCapacity(((MyCubeGrid)cubeGrid).BlocksCount);
                    cubeGrid.GetBlocks(blocks);
                    GatherInventory(blocks, workData);
                }

                workData.Inventories.SortNoAlloc((InventoryInfo x, InventoryInfo y) =>
                {
                    var comparison = x.Priority.CompareTo(y.Priority);
                    if (comparison == 0)
                    {
                        x.RealInventory?.Entity?.EntityId.CompareTo(y.RealInventory?.Entity?.EntityId);
                    }
                    return comparison;
                });

                BuildExcessItemMovement(workData);
                BuildDesiredItemMovement(workData);

                //MyLog.Default.WriteLineAndConsole($"CargoSort: Movement Data {workData.MovementData.Count} ops:\n{string.Join("\n", workData.MovementData)}");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"CargoSort: Sort failed with exception:\n{ex}");
            }
        }
        private void GatherInventory(List<IMySlimBlock> blocks, CargoSorterWorkData workData)
        {
            foreach (var block in blocks)
            {
                if (block.FatBlock == null)
                {
                    continue;
                }
                var fatBlock = block.FatBlock;
                if (!Util.IsValid(fatBlock) || fatBlock.GetInventory() == null || IsIgnored(fatBlock))
                {
                    continue;
                }

                for (int i = 0; i < fatBlock.InventoryCount; i++)
                {
                    var inventory = fatBlock.GetInventory(i) as MyInventory;
                    var inventoryInfo = new InventoryInfo(inventory);
                    workData.Inventories.Add(inventoryInfo);
                }
            }
        }
        private bool IsIgnored(IMyCubeBlock fatBlock)
        {
            var terminalBlock = fatBlock as IMyTerminalBlock;
            if (terminalBlock != null && !terminalBlock.HasLocalPlayerAccess())
            {
                return true;
            }
            foreach (var item in Instance.Config.LockedContainerKeywords)
            {
                if (fatBlock.DisplayNameText.Contains(item))
                {
                    return true;
                }
            }
            return false;
        }
        private void BuildExcessItemMovement(CargoSorterWorkData workData)
        {
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

                    foreach (var virtualItemKey in inventoryKeys)
                    {
                        MyFixedPoint virtualItemValue;
                        if (!sourceInventory.VirtualInventory.TryGetValue(virtualItemKey, out virtualItemValue))
                        {
                            continue;
                        }
                        var sourceAmountExcess = -CalculateAmountWanted(sourceInventory, virtualItemKey, virtualItemValue);

                        if (sourceAmountExcess > MyFixedPoint.Zero)
                        {
                            MyFixedPoint amountToBeMoved = MyFixedPoint.Min(CalculateAmountWanted(destInventory, virtualItemKey, virtualItemValue), sourceAmountExcess);
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
        }
        private void BuildDesiredItemMovement(CargoSorterWorkData workData)
        {
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
                    var destInventory = workData.Inventories[destInvIndex];
                    if (destInventory.TypeRequests.Equals(TypeRequests.Nothing) || destInvIndex == sourceInvIndex)
                    {
                        continue;
                    }

                    if (sourceInventory.Priority <= destInventory.Priority)
                    {
                        break;
                    }

                    var destPBInv = (VRage.Game.ModAPI.Ingame.IMyInventory)destInventory.RealInventory;
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv destination: {destInventory.Block?.DisplayNameText}");

                    foreach (var virtualItemKey in inventoryKeys)
                    {
                        MyFixedPoint virtualItemValue;
                        if (!sourceInventory.VirtualInventory.TryGetValue(virtualItemKey, out virtualItemValue))
                        {
                            continue;
                        }
                        MyFixedPoint amountToBeMoved = MyFixedPoint.Min(CalculateAmountWanted(destInventory, virtualItemKey, virtualItemValue), virtualItemValue);
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
        private MyFixedPoint CalculateAmountWanted(InventoryInfo inventoryInfo, MyDefinitionId definitionId, MyFixedPoint currentValue)
        {
            if (inventoryInfo.Constraint != null && !inventoryInfo.Constraint.Check(definitionId))
            {
                return -currentValue;
            }
            MyFixedPoint virtualAmount;
            switch (inventoryInfo.TypeRequests)
            {
                case TypeRequests.Nothing:
                    return -currentValue;
                case TypeRequests.GasGeneratorOre:
                    if (allBottles.Contains(definitionId) || inventoryInfo.Constraint == null || !inventoryInfo.Constraint.ConstrainedIds.Contains(definitionId))
                    {
                        return -currentValue;
                    }
                    inventoryInfo.VirtualInventory.TryGetValue(definitionId, out virtualAmount);
                    return (float)inventoryInfo.VirtualVolume / (float)inventoryInfo.MaxVolume > 0.7f
                        ? virtualAmount
                        : inventoryInfo.ComputeAmountThatCouldFit(definitionId, true, (float)inventoryInfo.MaxVolume * 0.2f, (float)inventoryInfo.MaxMass * 0.2f) - virtualAmount;
                case TypeRequests.AssemblerIngots:
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

                        if (constraintToCheck != null && constraintToCheck.ConstrainedIds.Contains(definitionId) && (double)inventoryInfo.VirtualVolume / (double)inventoryInfo.MaxVolume < 0.8)
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
                    return (double)inventoryInfo.VirtualVolume / (double)inventoryInfo.MaxVolume < 0.8 ? currentValue : -currentValue;
                case TypeRequests.GasTankBottles:
                    return -currentValue;
                case TypeRequests.SorterItems:
                    var sorter = inventoryInfo.RealInventory?.Entity as IMyConveyorSorter;
                    return sorter != null && sorter.DrainAll ? currentValue : -currentValue;
                case TypeRequests.ReactorFuel:
                    var reactor = inventoryInfo.RealInventory?.Entity as IMyReactor;
                    if (reactor != null)
                    {
                        var expectedAmount = (MyFixedPoint)((reactor.CubeGrid?.GridSizeEnum == MyCubeSize.Large ? 400f : 100f) * reactor.PowerOutputMultiplier);
                        inventoryInfo.VirtualInventory.TryGetValue(definitionId, out virtualAmount);
                        return virtualAmount < expectedAmount * 0.5f ? expectedAmount - virtualAmount : virtualAmount;
                    }
                    return MyFixedPoint.Zero;
                case TypeRequests.WeaponAmmo:
                    return inventoryInfo.ComputeAmountThatFits(definitionId);
                default:
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ores) && allOres.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ingots) && allIngots.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Components) && allComponents.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ammo) && allAmmo.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Tools) && allTools.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Bottles) && allBottles.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }

                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Special))
                    {
                        MyFixedPoint desiredAmount;
                        if (inventoryInfo.SpecialRequests.TryGetValue(definitionId, out desiredAmount))
                        {
                            inventoryInfo.VirtualInventory.TryGetValue(definitionId, out virtualAmount);
                            return MyFixedPoint.Min(inventoryInfo.ComputeAmountThatFits(definitionId, true), desiredAmount - virtualAmount);
                        }
                    }
                    return -currentValue;
            }
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

            ExecuteMovementData(workData);
        }
        private void ExecuteMovementData(CargoSorterWorkData workData)
        {
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
                    var toTransfer = MyFixedPoint.Min(item.Amount, movement.Amount);
                    MyInventory.TransferByUser(movement.Source.RealInventory, movement.Destination.RealInventory, item.ItemId, amount: toTransfer);
                    needToMove -= toTransfer;
                    if (needToMove <= MyFixedPoint.Zero)
                    {
                        break;
                    }
                }
            }
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