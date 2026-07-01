using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using InventorySorter.VirtualInventory;
using ParallelTasks;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;

namespace InventorySorter
{
    public static class SortingEngine
    {
        // Entry point — called from CargoSorterSessionComponent.BeginSortJob / BeginConstructSortJob
        public static void Run(WorkData data)
        {
            var workData = (CargoSorterWorkData)data;
            try
            {
                var inventories = new List<InventoryInfo>();
                if (workData.ConstructOnly)
                {
                    var grids = new HashSet<IMyCubeGrid>();
                    workData.RootGrid.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(grids);
                    foreach (var cubeGrid in grids)
                    {
                        if (!Util.IsValid(cubeGrid))
                        {
                            continue;
                        }

                        GatherInventory(cubeGrid.GetFatBlocks<IMyTerminalBlock>(), workData, inventories);
                    }
                }
                else
                {
                    var tree = new GridConnectorTree(workData.RootGrid);
                    var nodes = tree.GatherRecursive(c =>
                        c.CustomName?.InsensitiveContains("[nosort]") == false &&
                        c.OtherConnector?.CustomName?.InsensitiveContains("[nosort]") == false &&
                        c.OtherConnector?.CubeGrid?.CustomName?.InsensitiveContains("[nosort]") == false);

                    foreach (var cubeGrid in GridConnectorTree.GatherGrids(nodes))
                    {
                        if (!Util.IsValid(cubeGrid))
                        {
                            continue;
                        }

                        // MyLog.Default.WriteLineAndConsole($"Gathering inventories for {cubeGrid.CustomName}");
                        GatherInventory(cubeGrid.GetFatBlocks<IMyTerminalBlock>(), workData, inventories);
                    }
                }

                GenerateInventoryBuckets(inventories, workData);
                BuildExcessItemPool(workData);
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

        // Callback — called from CargoSorterSessionComponent.SortInventoryCallback
        public static void OnComplete(WorkData data)
        {
            var workData = (CargoSorterWorkData)data;
            CargoSorterSessionComponent.Instance.JobTask = new Task();
            var transferRequestCount = ExecuteMovementData(workData);
            DisplaySortResults(workData, transferRequestCount);
            CargoSorterSessionComponent.Instance.LastSortTick = MyAPIGateway.Session.GameplayFrameCounter;
            if (CargoSorterSessionComponent.Instance.AutoSortingController != null)
            {
                CargoSorterSessionComponent.Instance.AutoSortTicksRemaining = CargoSorterSessionComponent.Instance.Config.AutoSortFrequencySeconds * 60;
            }
        }

        private static void GatherInventory(IEnumerable<IMyTerminalBlock> blocks, CargoSorterWorkData workData, List<InventoryInfo> outInventories)
        {
            foreach (var block in blocks)
            {
                if (!Util.IsValid(block) || block.InventoryCount == 0 || !block.HasLocalPlayerAccess() || CargoSorterSessionComponent.Instance.IsIgnored(block))
                {
                    // MyLog.Default.WriteLineAndConsole($"Ignoring block: {Block.CustomName}");
                    continue;
                }

                for (var i = 0; i < block.InventoryCount; i++)
                {
                    var inventory = block.GetInventory(i) as MyInventory;
                    if (inventory == null)
                    {
                        continue;
                    }

                    var inventoryInfo = new InventoryInfo(inventory, workData.SectionName);
                    if (inventoryInfo.TypeRequests == TypeRequests.Nothing && (inventoryInfo.VirtualInventory.Count == 0 || !inventoryInfo.SupportsConveyors))
                    {
                        // MyLog.Default.WriteLineAndConsole($"Block wants nothing and has nothing, skipping: {Block.CustomName}");
                        continue;
                    }

                    // MyLog.Default.WriteLineAndConsole($"Adding inventory info for {Block.CustomName}");
                    outInventories.Add(inventoryInfo);
                    foreach (var definitionId in inventoryInfo.VirtualInventory.Keys)
                    {
                        if (!workData.TypeBuckets.ContainsKey(definitionId))
                        {
                            workData.TypeBuckets[definitionId] = new List<InventoryBucket>();
                        }
                    }

                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.SorterItems) && (inventoryInfo.Block as IMyConveyorSorter)?.DrainAll == true)
                    {
                        continue;
                    }

                    foreach (var item in inventoryInfo.VirtualInventory)
                    {
                        workData.AvailableForDistribution[item.Key] = workData.AvailableForDistribution.GetValueOrDefault(item.Key) + item.Value;
                    }

                    if ((inventoryInfo.TypeRequests.HasFlag(TypeRequests.Special) || inventoryInfo.TypeRequests.HasFlag(TypeRequests.Limited)) && inventoryInfo.Requests != null)
                    {
                        foreach (var request in inventoryInfo.Requests)
                        {
                            // Don't reserve for All containers
                            if (request.Flag == RequestFlags.All)
                            {
                                continue;
                            }

                            workData.AvailableForDistribution[request.DefinitionId] = workData.AvailableForDistribution.GetValueOrDefault(request.DefinitionId) - request.Amount;
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
                        if (def?.FuelInfos == null || def.FuelInfos.Length != 1)
                        {
                            continue;
                        }

                        var fuelInfo = def.FuelInfos.First();
                        var key = new ValueTuple<TypeRequests, MyDefinitionId>(TypeRequests.ReactorFuel, fuelInfo.FuelId);
                        workData.RequestTypeCount[key] = workData.RequestTypeCount.GetValueOrDefault(key) + 1;
                    }
                    else if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.ConsumableAmmo))
                    {
                        if (inventoryInfo.Constraint == null)
                        {
                            continue;
                        }

                        if (CargoSorterSessionComponent.Instance.IgnoredAmmoWeapons.Contains(inventoryInfo.Block.BlockDefinition))
                        {
                            continue;
                        }

                        if (!inventoryInfo.Block.IsFunctional || !CargoSorterSessionComponent.Instance.HasConveyorSupport(inventoryInfo.Block))
                        {
                            continue;
                        }

                        var wantedAmmo = GetActiveAmmo(inventoryInfo.Block);
                        if (wantedAmmo == default(MyDefinitionId) && inventory.Constraint?.ConstrainedIds != null && inventory.Constraint.ConstrainedIds.Count > 0)
                        {
                            // Take the first that's valid (excludes 5.56 old mags)
                            foreach (var id in inventory.Constraint.ConstrainedIds)
                            {
                                if (MyDefinitionManager.Static.GetAmmoMagazineDefinition(id)?.CanSpawnFromScreen != true)
                                    continue;
                                wantedAmmo = id;
                                break;
                            }
                        }

                        // Ignore weaponcore energy "ammo" or empty ammos which can happen if WC fails
                        if (wantedAmmo == default(MyDefinitionId) || wantedAmmo == CargoSorterSessionComponent.Instance.IgnoredEnergyAmmoDefinitionId)
                        {
                            continue;
                        }

                        var wantedAmount = inventoryInfo.ComputeAmountThatCouldFit(wantedAmmo, true);
                        if (wantedAmount <= MyFixedPoint.Zero || wantedAmount >= MyFixedPoint.MaxValue)
                        {
                            continue;
                        }

                        workData.AvailableForDistribution[wantedAmmo] = workData.AvailableForDistribution.GetValueOrDefault(wantedAmmo) - wantedAmount;
                    }
                }
            }
        }

        private static void GenerateInventoryBuckets(List<InventoryInfo> inventories, CargoSorterWorkData workData)
        {
            // MyLog.Default.WriteLineAndConsole($"Building inventory buckets");
            foreach (var inventory in inventories)
            {
                // MyLog.Default.WriteLineAndConsole($"Building buckets for inventory on {inventory.Block.CustomName}");
                foreach (var definitionId in workData.TypeBuckets.Keys)
                {
                    if (inventory.Constraint != null && !inventory.Constraint.Check(definitionId))
                    {
                        // MyLog.Default.WriteLineAndConsole($"Failed constraint check for {definitionId} on {inventory.Block.CustomName}, skipping");
                        continue;
                    }

                    var bucketFlags = InventoryBucketFlags.None;

                    if ((inventory.Block as IMyGasGenerator)?.AutoRefill == true || (inventory.Block as IMyGasTank)?.AutoRefillBottles == true)
                    {
                        bucketFlags |= InventoryBucketFlags.BottleFiller;
                    }

                    if (inventory.TypeRequests.HasFlag(TypeRequests.Special))
                    {
                        bucketFlags |= InventoryBucketFlags.Special;
                    }

                    if (inventory.TypeRequests.HasFlag(TypeRequests.Ores) && CargoSorterSessionComponent.Instance.AllOres.Contains(definitionId) ||
                        inventory.TypeRequests.HasFlag(TypeRequests.Ingots) && CargoSorterSessionComponent.Instance.AllIngots.Contains(definitionId) ||
                        inventory.TypeRequests.HasFlag(TypeRequests.Components) && CargoSorterSessionComponent.Instance.AllComponents.Contains(definitionId) ||
                        inventory.TypeRequests.HasFlag(TypeRequests.Ammo) && CargoSorterSessionComponent.Instance.AllAmmo.Contains(definitionId))
                    {
                        bucketFlags |= InventoryBucketFlags.Shuffle;
                    }

                    var buckets = workData.TypeBuckets[definitionId];
                    var bucket = buckets.Find(b => b.Priority == inventory.Priority && b.Flags == bucketFlags);
                    if (bucket == null)
                    {
                        bucket = new InventoryBucket(inventory.Priority, bucketFlags);
                        buckets.Add(bucket);
                    }

                    //MyLog.Default.WriteLineAndConsole($"Added inventory on {inventory.Block.CustomName} to {definitionId} bucket {bucket}");
                    bucket.Inventories.Add(inventory);
                }
            }

            // MyLog.Default.WriteLineAndConsole($"CargoSort: Ordering accepted index");
            var crc = new Crc32();
            foreach (var typeBuckets in workData.TypeBuckets)
            {
                var typeHash = unchecked((int)crc.GetCrc(typeBuckets.Key.ToString()));
                typeBuckets.Value.SortNoAlloc((x, y) =>
                {
                    // Fillers go first
                    var comparison = y.Flags.HasFlag(InventoryBucketFlags.BottleFiller).CompareTo(x.Flags.HasFlag(InventoryBucketFlags.BottleFiller));
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    // Specials go first
                    comparison = y.Flags.HasFlag(InventoryBucketFlags.Special).CompareTo(x.Flags.HasFlag(InventoryBucketFlags.Special));
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    // Priority applies next
                    comparison = x.Priority.CompareTo(y.Priority);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    // Shuffles go last
                    return x.Flags.HasFlag(InventoryBucketFlags.Shuffle).CompareTo(y.Flags.HasFlag(InventoryBucketFlags.Shuffle));
                });

                foreach (var bucket in typeBuckets.Value)
                {
                    bucket.Inventories.SortNoAlloc((x, y) =>
                    {
                        int comparison;
                        // Compare by entityID and type hash to shuffle when using broad types
                        if (bucket.Flags.HasFlag(InventoryBucketFlags.Shuffle))
                        {
                            var xHash = 17;
                            xHash = xHash * 31 + x.Block.EntityId.GetHashCode();
                            xHash = xHash * 31 + typeHash;

                            var yHash = 17;
                            yHash = yHash * 31 + y.Block.EntityId.GetHashCode();
                            yHash = yHash * 31 + typeHash;

                            comparison = xHash.CompareTo(yHash);
                            if (comparison != 0)
                            {
                                return comparison;
                            }
                        }

                        comparison = string.CompareOrdinal(x.Block.CustomName, y.Block.CustomName);
                        return comparison == 0 ? x.Block.EntityId.CompareTo(y.Block.EntityId) : comparison;
                    });
                }
            }

            // Debug dump
            // foreach (var typeBuckets in workData.TypeBuckets)
            // {
            //     MyLog.Default.WriteLineAndConsole($"CargoSort: {typeBuckets.Key}");
            //     foreach (var bucket in typeBuckets.Value)
            //     {
            //         MyLog.Default.WriteLineAndConsole($"CargoSort:   {bucket}");
            //         foreach (var i in bucket.Inventories)
            //         {
            //             MyLog.Default.WriteLineAndConsole($"CargoSort:     {i.Block.CustomName}");
            //         }
            //     }
            // }
        }

        private static bool ShouldUseBottleFillerLogic(InventoryInfo inventory, MyDefinitionId definitionId)
        {
            return inventory.TypeRequests.HasFlag(TypeRequests.GasBottles) &&
                   (definitionId.TypeId == typeof(MyObjectBuilder_OxygenContainerObject) || definitionId.TypeId == typeof(MyObjectBuilder_GasContainerObject));
        }

        private static void BuildExcessItemPool(CargoSorterWorkData workData)
        {
            foreach (var typeBucket in workData.TypeBuckets)
            {
                foreach (var bucket in typeBucket.Value)
                {
                    foreach (var inventory in bucket.Inventories)
                    {
                        var excess = inventory.VirtualInventory.GetValueOrDefault(typeBucket.Key);
                        if (ShouldUseBottleFillerLogic(inventory, typeBucket.Key))
                        {
                            var lowBottles = inventory.LowBottleCount?.GetValueOrDefault(typeBucket.Key) ?? MyFixedPoint.Zero;
                            if (lowBottles > MyFixedPoint.Zero)
                            {
                                // All full bottles are excess
                                excess -= lowBottles;
                            }
                        }
                        else
                        {
                            excess = -CalculateAmountWanted(inventory, typeBucket.Key, excess, workData);
                        }

                        if (excess <= MyFixedPoint.Zero)
                        {
                            continue;
                        }

                        var pool = workData.ExcessPools.GetValueOrNew(typeBucket.Key);
                        pool.Add(new ExcessInfo(inventory, excess));
                    }
                }
            }

            // Put the lowest priority inventories first so the highest priority can be popped off the end
            foreach (var pool in workData.ExcessPools)
            {
                pool.Value.Reverse();
            }
        }

        private static void BuildExcessItemMovement(CargoSorterWorkData workData)
        {
            // MyLog.Default.WriteLineAndConsole($"CargoSort (EXCESS): Removing excess items");
            foreach (var typeBucket in workData.TypeBuckets)
            {
                foreach (var bucket in typeBucket.Value)
                {
                    foreach (var destInventory in bucket.Inventories)
                    {
                        if (destInventory.TypeRequests.Equals(TypeRequests.Nothing) || destInventory.IsSatisfied)
                        {
                            continue;
                        }

                        VRage.Game.ModAPI.Ingame.IMyInventory destPbInv = destInventory.RealInventory;
                        // MyLog.Default.WriteLineAndConsole($"CargoSort (EXCESS): Inv destination: {typeBucket.Key} {bucket} {destInventory.Block?.DisplayNameText}");

                        List<ExcessInfo> pools;
                        if (!workData.ExcessPools.TryGetValue(typeBucket.Key, out pools))
                        {
                            continue;
                        }

                        if (pools.Count == 0)
                        {
                            workData.ExcessPools.Remove(typeBucket.Key);
                            if (workData.ExcessPools.Count == 0)
                            {
                                //MyLog.Default.WriteLineAndConsole($"CargoSort (EXCESS): No more excess items");
                                return;
                            }

                            continue;
                        }

                        var destCurrentAmount = destInventory.VirtualInventory.GetValueOrDefault(typeBucket.Key);
                        var amountWanted = CalculateAmountWanted(destInventory, typeBucket.Key, destCurrentAmount, workData);
                        // We don't want this item or we can't fit any more
                        if (amountWanted <= MyFixedPoint.Zero)
                        {
                            continue;
                        }

                        for (var i = pools.Count - 1; i >= 0; i--)
                        {
                            var inventoryExcess = pools[i];
                            // MyLog.Default.WriteLineAndConsole($"CargoSort (EXCESS): Remaining pools ({typeBucket.Key}): {pools.Count}");

                            var amountToBeMoved = MyFixedPoint.Min(amountWanted, inventoryExcess.Amount);

                            // Skip moving bottles if they're full and the target is a bottle filler
                            if (ShouldUseBottleFillerLogic(destInventory, typeBucket.Key))
                            {
                                var fullBottles = inventoryExcess.Inventory.VirtualInventory.GetValueOrDefault(typeBucket.Key) - (inventoryExcess.Inventory.LowBottleCount?.GetValueOrDefault(typeBucket.Key) ?? MyFixedPoint.Zero);
                                if (amountToBeMoved <= MyFixedPoint.Zero || inventoryExcess.Amount == fullBottles)
                                {
                                    continue;
                                }
                            }

                            //MyLog.Default.WriteLineAndConsole($"CargoSort: amountToBeMoved {pool.Key}: {amountToBeMoved} amountWanted {amountWanted}");
                            if (amountToBeMoved <= MyFixedPoint.Zero)
                            {
                                break;
                            }

                            MyFixedPoint volumeToBeMoved;
                            MyFixedPoint massToBeMoved;
                            if (!destInventory.CanItemsFit(amountToBeMoved, typeBucket.Key, out volumeToBeMoved, out massToBeMoved))
                            {
                                //MyLog.Default.WriteLineAndConsole($"CargoSort: Could not add {pool.Key} with amount {amountToBeMoved} to inventory");
                                break;
                            }

                            var sourceInventory = inventoryExcess.Inventory;
                            VRage.Game.ModAPI.Ingame.IMyInventory sourcePbInv = sourceInventory.RealInventory;

                            if (sourceInventory.VirtualInventory.GetValueOrDefault(typeBucket.Key) < amountToBeMoved)
                            {
                                //MyLog.Default.WriteLineAndConsole($"CargoSort: Source inventory {sourceInventory.Block?.DisplayNameText} is missing {pool.Key} with amount {amountToBeMoved} to inventory");
                                continue;
                            }

                            if (destInventory.SupportsConveyors && sourceInventory.SupportsConveyors && !sourcePbInv.CanTransferItemTo(destPbInv, typeBucket.Key))
                            {
                                continue;
                            }

                            // MyLog.Default.WriteLineAndConsole($"CargoSort (EXCESS): Inv source: {typeBucket.Key} {bucket} {sourceInventory.Block?.DisplayNameText}");
                            AppendInventoryOperation(workData, new InventoryMovement(sourceInventory, destInventory, typeBucket.Key, amountToBeMoved, volumeToBeMoved, massToBeMoved));
                            // Decrement the excess pool.
                            if (inventoryExcess.Amount <= amountToBeMoved)
                            {
                                //MyLog.Default.WriteLineAndConsole($"CargoSort: Pool {pool.Key} source {sourceInventory.Block?.DisplayNameText} empty, removing");
                                pools.RemoveAtFast(i);
                            }
                            else
                            {
                                //MyLog.Default.WriteLineAndConsole($"CargoSort: Pool {pool.Key} source {sourceInventory.Block?.DisplayNameText} lost some but not all, now {inventoryExcess.Item2}");
                                inventoryExcess.Amount -= amountToBeMoved;
                                pools[i] = inventoryExcess;
                            }

                            // Recalculate how much is needed and bail out of we don't want any more
                            destCurrentAmount = destInventory.VirtualInventory.GetValueOrDefault(typeBucket.Key);
                            amountWanted = CalculateAmountWanted(destInventory, typeBucket.Key, destCurrentAmount, workData);
                            if (amountWanted <= MyFixedPoint.Zero)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static void BuildDesiredItemMovement(CargoSorterWorkData workData)
        {
            // MyLog.Default.WriteLineAndConsole($"CargoSort: Moving desired items");
            foreach (var typeBucket in workData.TypeBuckets)
            {
                for (int sourceBucketIndex = typeBucket.Value.Count - 1; sourceBucketIndex >= 0; sourceBucketIndex--)
                {
                    var sourceBucket = typeBucket.Value[sourceBucketIndex];
                    for (int sourceInvIndex = sourceBucket.Inventories.Count - 1; sourceInvIndex >= 0; sourceInvIndex--)
                    {
                        var sourceInventory = sourceBucket.Inventories[sourceInvIndex];
                        VRage.Game.ModAPI.Ingame.IMyInventory sourcePbInv = sourceInventory.RealInventory;
                        MyFixedPoint virtualAmount;
                        // Nothing to transfer out
                        if (!sourceInventory.VirtualInventory.TryGetValue(typeBucket.Key, out virtualAmount) || virtualAmount <= MyFixedPoint.Zero)
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping source due to no items");
                            continue;
                        }

                        // Don't steal items from draining conveyor sorters, they'll just take them back.
                        if (sourceInventory.TypeRequests.HasFlag(TypeRequests.SorterItems) && (sourceInventory.Block as IMyConveyorSorter)?.DrainAll == true)
                        {
                            // MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping a conveyor sorter that's in drain mode type flags {sourceInventory.TypeRequests}");
                            continue;
                        }

                        // MyLog.Default.WriteLineAndConsole($"CargoSort: Inv source: {typeBucket.Key} {sourceBucket} {sourceInventory.Block?.DisplayNameText}");
                        for (var index = 0; index < typeBucket.Value.Count; index++)
                        {
                            var destBucket = typeBucket.Value[index];
                            foreach (var destInventory in destBucket.Inventories)
                            {
                                if (sourceInventory == destInventory)
                                {
                                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Source inv is same as dest inv, moving on: {destInventory.Block?.DisplayNameText}");
                                    goto nextInventory;
                                }

                                if (destInventory.TypeRequests.Equals(TypeRequests.Nothing) || destInventory.IsSatisfied)
                                {
                                    // MyLog.Default.WriteLineAndConsole($"CargoSort: Dest is satisfied, continuing: {destInventory.Block?.DisplayNameText}");
                                    continue;
                                }

                                if (sourceBucket.Flags.HasFlag(InventoryBucketFlags.Special) && (!CargoSorterSessionComponent.Instance.Config.AllowSpecialSteal || !destBucket.Flags.HasFlag(InventoryBucketFlags.Special)) && !destBucket.Flags.HasFlag(InventoryBucketFlags.BottleFiller))
                                {
                                    // MyLog.Default.WriteLineAndConsole($"CargoSort: Inv destination skipped due to not being special: {destInventory.Block?.DisplayNameText}");
                                    continue;
                                }

                                if (!sourceInventory.VirtualInventory.TryGetValue(typeBucket.Key, out virtualAmount) || virtualAmount <= MyFixedPoint.Zero)
                                {
                                    // MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping dest due to no remaining items: {destInventory.Block?.DisplayNameText}");
                                    goto nextInventory;
                                }

                                VRage.Game.ModAPI.Ingame.IMyInventory destPbInv = destInventory.RealInventory;

                                // MyLog.Default.WriteLineAndConsole($"CargoSort: Inv dest: {typeBucket.Key} {destBucket} {destInventory.Block?.DisplayNameText}");
                                var amountToBeMoved = CalculateAmountWanted(destInventory, typeBucket.Key, virtualAmount, workData);

                                // Clamp to low bottles only when moving to a bottle target
                                if (ShouldUseBottleFillerLogic(destInventory, typeBucket.Key))
                                {
                                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Low bottle wanted: {amountToBeMoved} | {sourceInventory.LowBottleCount?.GetValueOrDefault(typeBucket.Key) ?? MyFixedPoint.Zero}");
                                    amountToBeMoved = MyFixedPoint.Min(amountToBeMoved, sourceInventory.LowBottleCount?.GetValueOrDefault(typeBucket.Key) ?? MyFixedPoint.Zero);
                                }
                                else if (ShouldUseBottleFillerLogic(sourceInventory, typeBucket.Key))
                                {
                                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Full bottle wanted: {amountToBeMoved} | {virtualAmount - (sourceInventory.LowBottleCount?.GetValueOrDefault(typeBucket.Key) ?? MyFixedPoint.Zero)}");
                                    amountToBeMoved = MyFixedPoint.Min(amountToBeMoved, virtualAmount - (sourceInventory.LowBottleCount?.GetValueOrDefault(typeBucket.Key) ?? MyFixedPoint.Zero));
                                }
                                else
                                {
                                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Wanted: {amountToBeMoved} | {virtualAmount}");
                                    amountToBeMoved = MyFixedPoint.Min(amountToBeMoved, virtualAmount);
                                }

                                if (amountToBeMoved <= MyFixedPoint.Zero)
                                {
                                    // MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping dest due to zero movement: {destInventory.Block?.DisplayNameText}");
                                    continue;
                                }

                                if (destInventory.SupportsConveyors && sourceInventory.SupportsConveyors && !sourcePbInv.CanTransferItemTo(destPbInv, typeBucket.Key))
                                {
                                    // MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping dest due to conveyor failure: {destInventory.Block?.DisplayNameText}");
                                    continue;
                                }

                                // MyLog.Default.WriteLineAndConsole($"CargoSort: amountToBeMoved {virtualItemKey}: {amountToBeMoved}");

                                MyFixedPoint volumeToBeMoved;
                                MyFixedPoint massToBeMoved;
                                if (!destInventory.CanItemsFit(amountToBeMoved, typeBucket.Key, out volumeToBeMoved, out massToBeMoved))
                                {
                                    // MyLog.Default.WriteLineAndConsole($"CargoSort: Skipping dest due to not fitting: {destInventory.Block?.DisplayNameText}");
                                    continue;
                                }

                                sourceInventory.IsSatisfied = false;
                                AppendInventoryOperation(workData, new InventoryMovement(sourceInventory, destInventory, typeBucket.Key, amountToBeMoved, volumeToBeMoved, massToBeMoved));
                            }
                        }
                    }

                    nextInventory: ;
                }
            }
        }

        private static MyFixedPoint CalculateAmountWanted(InventoryInfo inventoryInfo, MyDefinitionId definitionId, MyFixedPoint currentValue, CargoSorterWorkData workData)
        {
            if (inventoryInfo.Constraint != null && !inventoryInfo.Constraint.Check(definitionId))
            {
                return -currentValue;
            }

            var percentFull = (float)inventoryInfo.VirtualVolume / (float)inventoryInfo.MaxVolume;

            var typeRequests = inventoryInfo.TypeRequests;

            if (typeRequests == TypeRequests.Nothing)
            {
                return -currentValue;
            }

            if (typeRequests.HasFlag(TypeRequests.GasBottles))
            {
                if (definitionId.TypeId == typeof(MyObjectBuilder_OxygenContainerObject) || definitionId.TypeId == typeof(MyObjectBuilder_GasContainerObject))
                {
                    var lowBottleCount = inventoryInfo.LowBottleCount?.GetValueOrDefault(definitionId) ?? MyFixedPoint.Zero;
                    var bottleCount = inventoryInfo.VirtualInventory.GetValueOrDefault(definitionId);
                    var physItem = MyDefinitionManager.Static.GetPhysicalItemDefinition(definitionId);
                    var canFit = physItem == null ? MyFixedPoint.Zero : inventoryInfo.ComputeAmountThatFits(physItem, true);
                    if (inventoryInfo.Block is IMyGasGenerator && CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent > 0)
                    {
                        canFit = MyFixedPoint.Min(canFit, inventoryInfo.ComputeAmountThatCouldFit(physItem, true,
                            MyFixedPoint.Min(inventoryInfo.VirtualVolume, inventoryInfo.MaxVolume * (1f - CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent)),
                            MyFixedPoint.Min(inventoryInfo.VirtualMass, inventoryInfo.MaxMass * (1f - CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent))));
                    }

                    return canFit - bottleCount + lowBottleCount;
                }

                if (inventoryInfo.Block is IMyGasTank)
                {
                    return -currentValue;
                }
            }

            if (typeRequests.HasFlag(TypeRequests.GasGeneratorOre))
            {
                if ((inventoryInfo.Constraint != null && !inventoryInfo.Constraint.Check(definitionId)) ||
                    definitionId.TypeId == typeof(MyObjectBuilder_OxygenContainerObject) ||
                    definitionId.TypeId == typeof(MyObjectBuilder_GasContainerObject))
                {
                    return -currentValue;
                }

                // <= 0 disables the feature
                if (CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent <= 0)
                {
                    return MyFixedPoint.Zero;
                }

                if (percentFull < CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent / 2f || percentFull > 1f - ((1f - CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent) / 2f))
                {
                    return inventoryInfo.ComputeAmountThatCouldFit(definitionId, true,
                        inventoryInfo.MaxVolume * (1f - CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent),
                        inventoryInfo.MaxMass * (1f - CargoSorterSessionComponent.Instance.Config.GasGeneratorFillPercent)
                    ) - inventoryInfo.VirtualInventory.GetValueOrDefault(definitionId);
                }

                return MyFixedPoint.Zero;
            }

            if (typeRequests == TypeRequests.AssemblerIngots)
            {
                var assembler = inventoryInfo.Block as Sandbox.ModAPI.Ingame.IMyAssembler;
                // Make sure the output inventory is clear in normal operation.
                if (assembler == null || !assembler.IsProducing || !assembler.Enabled)
                {
                    return -currentValue;
                }

                MyInventoryConstraint constraintToCheck = null;
                if (assembler.Mode == Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly)
                {
                    if (inventoryInfo.RealInventory != assembler.InputInventory)
                    {
                        // Always clear output side when assembling
                        return -currentValue;
                    }

                    constraintToCheck = ((MyInventory)assembler.InputInventory)?.Constraint;
                }
                else if (assembler.Mode == Sandbox.ModAPI.Ingame.MyAssemblerMode.Disassembly)
                {
                    if (inventoryInfo.RealInventory != assembler.OutputInventory)
                    {
                        // Always clear input side when disassembling
                        return -currentValue;
                    }

                    constraintToCheck = ((MyInventory)assembler.OutputInventory)?.Constraint;
                }

                if (constraintToCheck == null || !constraintToCheck.Check(definitionId))
                {
                    return -currentValue;
                }

                var efficiencyMultiplier = MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
                MyFixedPoint newAmount = -currentValue;
                // Crawl the queue's blueprints to see if what we have is what we need, and get rid of stuff we don't need.
                var items = new List<Sandbox.ModAPI.Ingame.MyProductionItem>();
                assembler.GetQueue(items);
                foreach (var queuedItem in items)
                {
                    var blueprint = MyDefinitionManager.Static.GetBlueprintDefinition(queuedItem.BlueprintId);
                    if (blueprint == null)
                    {
                        continue;
                    }

                    foreach (var prerequisite in blueprint.Prerequisites)
                    {
                        if (prerequisite.Id != definitionId)
                        {
                            continue;
                        }

                        newAmount += prerequisite.Amount * queuedItem.Amount * (1 / efficiencyMultiplier);
                    }
                }

                // Let the assembler pull if it can and needs more so that there's no situation
                // where one assembler hogs all the material due to queued items.
                return assembler.UseConveyorSystem && newAmount > MyFixedPoint.Zero ? MyFixedPoint.Zero : newAmount;

                // If the assembler is off or full somehow, just take everything out.
            }

            if (typeRequests == TypeRequests.RefineryOre)
            {
                var refinery = inventoryInfo.Block as IMyRefinery;
                if (refinery != null)
                {
                    var inputConstraint = ((MyInventory)refinery.InputInventory)?.Constraint;
                    if (inventoryInfo.RealInventory == refinery.InputInventory && inputConstraint != null && inputConstraint.Check(definitionId))
                    {
                        // Only clear the refinery input if the refinery is off
                        return refinery.IsProducing && refinery.Enabled ? MyFixedPoint.Zero : -currentValue;
                    }
                }

                // If this is the refinery output, or the refinery is off or full somehow, take everything out.
                return -currentValue;
            }

            if (typeRequests.HasFlag(TypeRequests.SorterItems))
            {
                var sorter = inventoryInfo.Block as IMyConveyorSorter;
                if (sorter != null)
                {
                    if (sorter.DrainAll)
                    {
                        return MyFixedPoint.Zero;
                    }

                    if (typeRequests == TypeRequests.SorterItems) // If there are no other flags to handle, just empty it
                    {
                        return -currentValue;
                    }
                }
                else
                {
                    return -currentValue;
                }
            }

            if (typeRequests == TypeRequests.ReactorFuel)
            {
                var reactor = inventoryInfo.Block as IMyReactor;
                if (reactor == null)
                {
                    return -currentValue;
                }

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

                var configuredExpected = reactor.CubeGrid?.GridSizeEnum == MyCubeSize.Large ? CargoSorterSessionComponent.Instance.Config.ExpectedLargeGridReactorFuel : CargoSorterSessionComponent.Instance.Config.ExpectedSmallGridReactorFuel;

                // <= 0 disables the feature, also not used if the reactor self fills
                if (configuredExpected <= 0 || reactor.UseConveyorSystem)
                {
                    return MyFixedPoint.Zero;
                }

                var expectedAmount = (MyFixedPoint)Math.Min(
                    (float)availableForDistribution / (float)typeRequestCount,
                    ((float)configuredExpected * reactor.PowerOutputMultiplier)
                );
                //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel {inventoryInfo.Block?.DisplayNameText} expectedAmount {expectedAmount}");
                var virtualAmount = inventoryInfo.VirtualInventory.GetValueOrDefault(definitionId);

                if (virtualAmount < expectedAmount * 0.5f)
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel too little, returning ({expectedAmount} - {virtualAmount}) {expectedAmount - virtualAmount}");
                    return expectedAmount - virtualAmount;
                }

                if (currentValue > expectedAmount)
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel too much, returning ({expectedAmount} - {currentValue}) {expectedAmount - currentValue}");
                    return expectedAmount - currentValue;
                }

                //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel in range, returning 0 wanted");
                return MyFixedPoint.Zero;
            }

            if (typeRequests == TypeRequests.ConsumableAmmo)
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            // If additional flags exist, let them fall to other cases
            if (typeRequests.HasFlag(TypeRequests.Special))
            {
                // MyLog.Default.WriteLineAndConsole($"CargoSort: Special request amount {definitionId} {GetRequestAmount(inventoryInfo, definitionId, currentValue)} {inventoryInfo.ComputeAmountThatFits(definitionId, true)}");
                return GetRequestAmount(inventoryInfo, definitionId, currentValue);
            }

            // The check for null requests and findindex lets this fall through to next if there's no request that matches.
            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Limited) && inventoryInfo.Requests != null && inventoryInfo.Requests.FindIndex(r => r.DefinitionId == definitionId) > -1)
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Limited request amount {definitionId} {GetRequestAmount(inventoryInfo, definitionId, currentValue)}");
                return GetRequestAmount(inventoryInfo, definitionId, currentValue);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ores) && CargoSorterSessionComponent.Instance.AllOres.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ingots) && CargoSorterSessionComponent.Instance.AllIngots.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Components) && CargoSorterSessionComponent.Instance.AllComponents.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ammo) && CargoSorterSessionComponent.Instance.AllAmmo.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Tools) && CargoSorterSessionComponent.Instance.AllTools.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Bottles) && CargoSorterSessionComponent.Instance.AllBottles.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Consumables) && CargoSorterSessionComponent.Instance.AllConsumables.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ingredients) && CargoSorterSessionComponent.Instance.AllIngredients.Contains(definitionId))
            {
                return inventoryInfo.ComputeAmountThatFits(definitionId);
            }

            return -currentValue;
        }

        private static MyFixedPoint GetRequestAmount(InventoryInfo inventoryInfo, MyDefinitionId definitionId, MyFixedPoint currentValue)
        {
            if (inventoryInfo.Requests == null)
            {
                return -currentValue;
            }

            var requestIndex = inventoryInfo.Requests.FindIndex(r => r.DefinitionId == definitionId);
            if (requestIndex == -1)
            {
                return -currentValue;
            }

            var requestInfo = inventoryInfo.Requests[requestIndex];
            var virtualAmount = inventoryInfo.VirtualInventory.GetValueOrDefault(definitionId);

            if (requestInfo.Flag <= RequestFlags.Max ||
                (requestInfo.Flag == RequestFlags.Limit && virtualAmount > requestInfo.Amount) ||
                (requestInfo.Flag == RequestFlags.Minimum && virtualAmount < requestInfo.Amount))
            {
                return MyFixedPoint.Min(inventoryInfo.ComputeAmountThatFits(definitionId, true), requestInfo.Amount - virtualAmount);
            }

            return MyFixedPoint.Zero;
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
            // Sync LowBottleCount for GasBottles
            if (operation.Item.TypeId == typeof(MyObjectBuilder_GasContainerObject) || operation.Item.TypeId == typeof(MyObjectBuilder_OxygenContainerObject))
            {
                if (operation.Destination.TypeRequests.HasFlag(TypeRequests.GasBottles))
                {
                    // Moving TO bottle filler - all items are low bottles, decrement source LowBottleCount
                    if (operation.Source.LowBottleCount?.ContainsKey(operation.Item) == true)
                    {
                        var sourceLowAmount = operation.Source.LowBottleCount[operation.Item] - operation.Amount;
                        if (sourceLowAmount <= MyFixedPoint.Zero)
                        {
                            operation.Source.LowBottleCount.Remove(operation.Item);
                        }
                        else
                        {
                            operation.Source.LowBottleCount[operation.Item] = sourceLowAmount;
                        }
                    }

                    // Increment destination LowBottleCount for low bottles received
                    if (operation.Destination.LowBottleCount == null)
                    {
                        operation.Destination.LowBottleCount = new Dictionary<MyDefinitionId, MyFixedPoint>();
                    }
                    operation.Destination.LowBottleCount[operation.Item] = operation.Destination.LowBottleCount.GetValueOrDefault(operation.Item, MyFixedPoint.Zero) + operation.Amount;
                }
            }

            operation.Destination.VirtualVolume += operation.Volume;
            operation.Destination.VirtualMass += operation.Mass;
            MyFixedPoint destChangedAmount = operation.Destination.VirtualInventory.GetValueOrDefault(operation.Item) + operation.Amount;
            operation.Destination.VirtualInventory[operation.Item] = destChangedAmount;
            workData.MovementData.Add(operation);
            // MyLog.Default.WriteLineAndConsole($"CargoSort: Added operation: {operation.Source.Block?.DisplayNameText} -> {operation.Destination.Block?.DisplayNameText} | {operation.Amount} {operation.Item}");
        }

        private static void DisplaySortResults(CargoSorterWorkData workData, int transferRequestCount)
        {
            var validationFailedBlocks = new Dictionary<IMyTerminalBlock, ValueTuple<RequestValidationStatus, MyIniParseResult>>();
            foreach (var typeBucket in workData.TypeBuckets)
            {
                foreach (var inventoryBucket in typeBucket.Value)
                {
                    foreach (var inventory in inventoryBucket.Inventories)
                    {
                        if (inventory.RequestStatus == RequestValidationStatus.Valid || !Util.IsValid(inventory.Block))
                        {
                            continue;
                        }

                        validationFailedBlocks[inventory.Block] = new ValueTuple<RequestValidationStatus, MyIniParseResult>(inventory.RequestStatus, inventory.ConfigParseResult);
                    }
                }
            }

            var duration = (DateTime.UtcNow - workData.StartTime).TotalSeconds;

            switch (workData.ResultsType)
            {
                case ResultsDisplayType.Chat:
                    if (CargoSorterSessionComponent.Instance.Config.ShowProgressNotifications)
                    {
                        if (duration > 0.5)
                        {
                            if (transferRequestCount == 0)
                            {
                                MyAPIGateway.Utilities.ShowMessage("Sorter", $"No transfers needed ({Math.Round((DateTime.UtcNow - workData.StartTime).TotalSeconds, 2)}s).");
                            }
                            else
                            {
                                MyAPIGateway.Utilities.ShowMessage("Sorter", $"{transferRequestCount} transfers requested ({Math.Round((DateTime.UtcNow - workData.StartTime).TotalSeconds, 2)}s).");
                            }
                        }
                        else
                        {
                            if (transferRequestCount == 0)
                            {
                                MyAPIGateway.Utilities.ShowMessage("Sorter", $"No transfers needed.");
                            }
                            else
                            {
                                MyAPIGateway.Utilities.ShowMessage("Sorter", $"{transferRequestCount} transfers requested.");
                            }
                        }
                    }

                    foreach (var failedBlock in validationFailedBlocks)
                    {
                        if (failedBlock.Value.Item1.HasFlag(RequestValidationStatus.TooMuchVolume))
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"Warning: Requested items on '{failedBlock.Key.CustomName}' will not fit!");
                        }

                        if (failedBlock.Value.Item1.HasFlag(RequestValidationStatus.InvalidCustomData))
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"Invalid Custom Data on container '{failedBlock.Key.CustomName}': {failedBlock.Value.Item2.Error}");
                        }
                        else if (failedBlock.Value.Item1.HasFlag(RequestValidationStatus.InvalidItem) || failedBlock.Value.Item1.HasFlag(RequestValidationStatus.InvalidCount))
                        {
                            var ini = new MyIni();
                            var terminalBlock = failedBlock.Key;
                            if (!Util.IsValid(terminalBlock) || !ini.TryParse(terminalBlock.CustomData))
                            {
                                continue;
                            }

                            if (!ini.ContainsSection("Inventory"))
                            {
                                continue;
                            }

                            List<MyIniKey> iniKeys = new List<MyIniKey>();
                            ini.GetKeys("Inventory", iniKeys);

                            foreach (var iniKey in iniKeys)
                            {
                                if (iniKey.IsEmpty)
                                {
                                    continue;
                                }

                                MyDefinitionId definitionId;
                                if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                                {
                                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Unknown item '{iniKey.Name}' in Custom Data on container '{terminalBlock.CustomName}'");
                                    continue;
                                }

                                var failedConstraints = false;
                                for (var i = 0; i < failedBlock.Key.InventoryCount; i++)
                                {
                                    var inventory = failedBlock.Key.GetInventory(i) as MyInventory;
                                    if (inventory?.Constraint == null)
                                    {
                                        break;
                                    }

                                    if (inventory.Constraint.Check(definitionId))
                                    {
                                        continue;
                                    }

                                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"'{definitionId}' in Custom Data on container '{terminalBlock.CustomName}' is not allowed in inventory {i} on the block");
                                    failedConstraints = true;
                                    break;
                                }

                                if (failedConstraints)
                                {
                                    continue;
                                }

                                var value = ini.Get(iniKey);
                                var valueString = value.ToString();
                                if (string.IsNullOrWhiteSpace(valueString) || valueString.Equals("All", StringComparison.OrdinalIgnoreCase) || valueString.Equals("Max", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                int itemCount;
                                if (!int.TryParse(valueString.TrimEnd('%', 'l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                                {
                                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Invalid count '{valueString}' for type '{iniKey.Name}' in Custom Data on container '{terminalBlock.CustomName}'");
                                }
                            }
                        }
                    }

                    if (CargoSorterSessionComponent.Instance.Config.ShowMissingItems)
                    {
                        foreach (var availability in workData.AvailableForDistribution.OrderByDescending(kv => (float)kv.Value))
                        {
                            if (availability.Value >= MyFixedPoint.Zero)
                            {
                                continue;
                            }

                            var def = MyDefinitionManager.Static.GetDefinition(availability.Key);
                            if (def == null)
                            {
                                continue;
                            }

                            CargoSorterSessionComponent.Instance.LastMissingItems.Clear();
                            foreach (var item in workData.AvailableForDistribution)
                            {
                                if (item.Value >= MyFixedPoint.Zero)
                                {
                                    continue;
                                }

                                CargoSorterSessionComponent.Instance.LastMissingItems[item.Key] = MyFixedPoint.Ceiling(-item.Value);
                            }

                            MyAPIGateway.Utilities.ShowMessage("Needed", $"{MyFixedPoint.Ceiling(-availability.Value)}x {def.DisplayNameText}");
                        }
                    }

                    break;
                case ResultsDisplayType.Window:
                    var groups = new Dictionary<string, Dictionary<MyDefinitionId, MyFixedPoint>>();
                    StringBuilder warningsBuilder = null;
                    if (validationFailedBlocks.Count > 0)
                    {
                        warningsBuilder = new StringBuilder();

                        foreach (var failedBlock in validationFailedBlocks)
                        {
                            warningsBuilder.AppendFormat("{0}:\n", failedBlock.Key.CustomName);

                            if (failedBlock.Value.Item1.HasFlag(RequestValidationStatus.TooMuchVolume))
                            {
                                warningsBuilder.AppendLine("The block's Custom Data requests more items than can possibly fit in its inventory. Reduce the number of items desired or move the Custom Data, tag and priority to a block with more inventory space.");
                            }

                            if (failedBlock.Value.Item1.HasFlag(RequestValidationStatus.InvalidCustomData))
                            {
                                warningsBuilder.AppendLine($"The block's Custom Data was not able to be interpreted as an inventory request. Clear the block's Custom Data and set it up again or remove the Limited/Special tag: {failedBlock.Value.Item2.Error}");
                            }
                            else if (failedBlock.Value.Item1.HasFlag(RequestValidationStatus.InvalidItem) || failedBlock.Value.Item1.HasFlag(RequestValidationStatus.InvalidCount))
                            {
                                warningsBuilder.AppendLine("These lines in the block's Custom Data are not valid:");
                                var ini = new MyIni();
                                var terminalBlock = failedBlock.Key;
                                if (!Util.IsValid(terminalBlock) || !ini.TryParse(terminalBlock.CustomData))
                                {
                                    continue;
                                }

                                if (!ini.ContainsSection("Inventory"))
                                {
                                    continue;
                                }

                                List<MyIniKey> iniKeys = new List<MyIniKey>();
                                ini.GetKeys("Inventory", iniKeys);

                                foreach (var iniKey in iniKeys)
                                {
                                    if (iniKey.IsEmpty)
                                    {
                                        continue;
                                    }

                                    MyDefinitionId definitionId;
                                    if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                                    {
                                        warningsBuilder.AppendFormat("Unknown item: {0}", iniKey.Name).AppendLine();
                                        continue;
                                    }

                                    var failedConstraints = false;
                                    for (var i = 0; i < failedBlock.Key.InventoryCount; i++)
                                    {
                                        var inventory = failedBlock.Key.GetInventory(i) as MyInventory;
                                        if (inventory?.Constraint == null)
                                        {
                                            break;
                                        }

                                        if (inventory.Constraint.Check(definitionId))
                                        {
                                            continue;
                                        }

                                        MyAPIGateway.Utilities.ShowMessage("Sorter", $"{definitionId} not allowed in inventory {i}");
                                        failedConstraints = true;
                                        break;
                                    }

                                    if (failedConstraints)
                                    {
                                        continue;
                                    }

                                    var value = ini.Get(iniKey);
                                    var valueString = value.ToString();
                                    if (string.IsNullOrWhiteSpace(valueString) || valueString.Equals("All", StringComparison.OrdinalIgnoreCase) || valueString.Equals("Max", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    int itemCount;
                                    if (!int.TryParse(valueString.TrimEnd('%', 'l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                                    {
                                        warningsBuilder.AppendFormat("Invalid requested value: '{0}' for type '{1}'", valueString, iniKey.Name).AppendLine();
                                    }
                                }
                            }

                            warningsBuilder.AppendLine();
                        }

                        Util.TrimTrailingWhitespace(warningsBuilder);
                    }

                    foreach (var availability in workData.AvailableForDistribution)
                    {
                        if (availability.Value >= MyFixedPoint.Zero)
                        {
                            continue;
                        }

                        string friendlyName = CargoSorterSessionComponent.Instance.GetFriendlyTypeName(availability.Key);
                        var group = groups.GetValueOrNew(friendlyName);
                        group[availability.Key] = group.GetValueOrDefault(availability.Key) + availability.Value;
                        groups[friendlyName] = group;
                    }

                    var displayStringBuilder = new StringBuilder();

                    if (transferRequestCount == 0)
                    {
                        displayStringBuilder.Append("No transfers needed.");
                    }
                    else
                    {
                        displayStringBuilder.AppendFormat("{0} transfers requested.", transferRequestCount);
                    }

                    if (duration > 0.5)
                    {
                        displayStringBuilder.AppendLine();
                        displayStringBuilder.AppendFormat("Duration: {0}s", Math.Round((DateTime.UtcNow - workData.StartTime).TotalSeconds, 2));
                    }

                    if (groups.Count > 0 || warningsBuilder != null)
                    {
                        displayStringBuilder.AppendLine();
                        displayStringBuilder.AppendLine();
                        if (warningsBuilder != null)
                        {
                            displayStringBuilder.AppendLine("Warnings:");
                            displayStringBuilder.AppendStringBuilder(warningsBuilder);
                            displayStringBuilder.AppendLine();
                            displayStringBuilder.AppendLine();
                        }

                        if (groups.Count > 0)
                        {
                            displayStringBuilder.AppendLine("Missing Items:");
                            foreach (var group in groups.OrderBy(g => g.Key))
                            {
                                displayStringBuilder.AppendFormat("{0}:\n", group.Key);
                                foreach (var subTypeValue in group.Value.OrderBy(g => (float)g.Value))
                                {
                                    displayStringBuilder.AppendFormat("{0}: {1}\n", CargoSorterSessionComponent.Instance.GetFriendlyDefinitionDisplayName(subTypeValue.Key), MyFixedPoint.Ceiling(-subTypeValue.Value));
                                }

                                displayStringBuilder.AppendLine();
                            }
                        }

                        Util.TrimTrailingWhitespace(displayStringBuilder);
                    }

                    var stringToShow = "Sorting Complete";

                    if (warningsBuilder != null && groups.Count > 0)
                    {
                        stringToShow = "Warnings and Missing Items";
                    }
                    else if (warningsBuilder == null && groups.Count > 0)
                    {
                        stringToShow = "Missing Items";
                    }
                    else if (warningsBuilder != null && groups.Count == 0)
                    {
                        stringToShow = "Warnings";
                    }

                    MyAPIGateway.Utilities.ShowMissionScreen("Inventory Sorter", string.Empty, stringToShow, displayStringBuilder.ToString(), (clickResult) =>
                    {
                        if (CargoSorterSessionComponent.Instance.Config.CopyResultsToClipboard && groups.Count > 0 && clickResult == ResultEnum.OK)
                        {
                            var clipboardStringBuilder = new StringBuilder();
                            foreach (var group in groups.OrderBy(g => g.Key))
                            {
                                foreach (var subTypeValue in group.Value.OrderBy(g => (float)g.Value))
                                {
                                    clipboardStringBuilder.AppendFormat("{0},{1},{2}\n", group.Key, subTypeValue.Key.SubtypeId, MyFixedPoint.Ceiling(-subTypeValue.Value));
                                }
                            }

                            MyClipboardHelper.SetClipboard(clipboardStringBuilder.ToString());
                        }
                    }, CargoSorterSessionComponent.Instance.Config.CopyResultsToClipboard && groups.Count > 0 ? "Copy to Clipboard" : null);

                    break;
            }
        }

        private static int ExecuteMovementData(CargoSorterWorkData workData)
        {
            int transferRequests = 0;
            foreach (var movement in workData.MovementData)
            {
                if (!Util.IsValid(movement.Source.Block) || !Util.IsValid(movement.Destination.Block))
                {
                    continue;
                }

                var items = movement.Source.RealInventory.GetItems();
                var needToMove = movement.Amount;
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    var item = items[i];
                    if (item.Content.GetId() != movement.Item)
                    {
                        continue;
                    }

                    if (ShouldUseBottleFillerLogic(movement.Destination, movement.Item))
                    {
                        var gasContainer = item.Content as MyObjectBuilder_GasContainerObject;
                        if (gasContainer != null)
                        {
                            if (gasContainer.GasLevel > 0.99)
                            {
                                continue;
                            }
                        }
                    }
                    else if (ShouldUseBottleFillerLogic(movement.Source, movement.Item))
                    {
                        var gasContainer = item.Content as MyObjectBuilder_GasContainerObject;
                        if (gasContainer != null)
                        {
                            if (gasContainer.GasLevel < 1)
                            {
                                continue;
                            }
                        }
                    }

                    var toTransfer = MyFixedPoint.Min(item.Amount, needToMove);
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Movement from: {movement.Source.Block?.DisplayNameText} ({movement.Source.TypeRequests}, P{movement.Source.Priority}) To: {movement.Destination.Block?.DisplayNameText} ({movement.Destination.TypeRequests}, P{movement.Destination.Priority}): {item.Content.TypeId}/{item.Content.SubtypeName} {toTransfer}");
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

        // Helper methods that were private in session component — kept as static here since they don't need instance access
        private static MyDefinitionId GetActiveAmmo(IMyTerminalBlock weapon)
        {
            if (!Util.IsValid(weapon) || CargoSorterSessionComponent.Instance.WcAmmoMagazines.Count == 0 || !CargoSorterSessionComponent.Instance.WcApi.IsReady)
            {
                return default(MyDefinitionId);
            }

            var activeAmmo = CargoSorterSessionComponent.Instance.WcApi.GetActiveAmmo(weapon as MyEntity, 0);

            if (string.IsNullOrEmpty(activeAmmo))
            {
                return default(MyDefinitionId);
            }

            return CargoSorterSessionComponent.Instance.WcAmmoMagazines.GetValueOrDefault(activeAmmo);
        }
    }
}
