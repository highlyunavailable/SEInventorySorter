﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CargoSorter.Data.Scripts.CargoSorter;
using CoreSystems.Api;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
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

        private readonly Dictionary<MyDefinitionId, float> allVolumes = new Dictionary<MyDefinitionId, float>();
        private readonly static Dictionary<MyDefinitionId, bool> blockConveyorSupport = new Dictionary<MyDefinitionId, bool>();

        private readonly WcApi wcApi = new WcApi();
        private readonly HashSet<MyDefinitionId> sorters = new HashSet<MyDefinitionId>();
        private readonly Dictionary<string, MyDefinitionId> wcAmmoMagazines = new Dictionary<string, MyDefinitionId>();
        private static readonly MyDefinitionId IgnoredEnergyAmmoDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), "Energy");

        private readonly Dictionary<string, MyDefinitionId> stringPhysicalItemMap = new Dictionary<string, MyDefinitionId>(StringComparer.InvariantCultureIgnoreCase);
        private readonly Dictionary<MyObjectBuilderType, string> friendlyTypeNames = new Dictionary<MyObjectBuilderType, string>();

        private Task jobTask;

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
                if (definition.IsOre)
                {
                    allOres.Add(definition.Id);
                    allVolumes.Add(definition.Id, definition.Volume);
                    MakeNormalizedId(definition.Id, "Ore");
                }
                if (definition.IsIngot)
                {
                    allIngots.Add(definition.Id);
                    allVolumes.Add(definition.Id, definition.Volume);
                    MakeNormalizedId(definition.Id, "Ingot");
                }
                if (definition is MyUsableItemDefinition || definition is MyDatapadDefinition || definition is MyPackageDefinition || definition.Id.TypeId == typeof(MyObjectBuilder_PhysicalObject))
                {
                    allTools.Add(definition.Id);
                    allVolumes.Add(definition.Id, definition.Volume);
                    MakeNormalizedId(definition.Id, "Item");
                }
                if (definition is MyOxygenContainerDefinition)
                {
                    allBottles.Add(definition.Id);
                    allVolumes.Add(definition.Id, definition.Volume);
                    MakeNormalizedId(definition.Id, "Bottle");
                }
                if (definition is MyComponentDefinition)
                {
                    allComponents.Add(definition.Id);
                    allVolumes.Add(definition.Id, definition.Volume);
                    MakeNormalizedId(definition.Id, "Component");
                }
                if (definition is MyAmmoMagazineDefinition)
                {
                    allAmmo.Add(definition.Id);
                    allVolumes.Add(definition.Id, definition.Volume);
                    MakeNormalizedId(definition.Id, "Ammo");
                }
            }
            foreach (var definition in MyDefinitionManager.Static.GetHandItemDefinitions())
            {
                if (!definition.Enabled || !definition.Public)
                {
                    continue;
                }
                var handPhysicalItem = MyDefinitionManager.Static.GetPhysicalItemForHandItem(definition.Id);
                if (handPhysicalItem != null && handPhysicalItem.Enabled && handPhysicalItem.Public)
                {
                    MakeNormalizedId(handPhysicalItem.Id, "Tool");
                    allTools.Add(handPhysicalItem.Id);
                    allVolumes.Add(handPhysicalItem.Id, handPhysicalItem.Volume);
                }
            }

            foreach (var def in MyDefinitionManager.Static.GetDefinitionsOfType<MyConveyorSorterDefinition>())
            {
                sorters.Add(def.Id);
            }

            //foreach (var item in friendlyTypeNames)
            //{
            //    MyLog.Default.WriteLineAndConsole($"CargoSort: Friendly type {item.Key} -> {item.Value}");
            //}
            //foreach (var item in stringPhysicalItemMap)
            //{
            //    MyLog.Default.WriteLineAndConsole($"CargoSort: Normalized ID {item.Key} -> {item.Value}");
            //}
        }

        public override void BeforeStart()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
            if (!wcApi.IsReady)
            {
                wcApi.Load(OnWcReady, true);
            }
        }

        private void OnWcReady()
        {
            var allCoreWeapons = new HashSet<MyDefinitionId>();
            wcApi.GetAllCoreWeapons(allCoreWeapons);
            sorters.ExceptWith(allCoreWeapons);

            var allWeaponMagazines = new Dictionary<MyDefinitionId, List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>>>();
            wcApi.GetAllWeaponMagazines(allWeaponMagazines);
            foreach (var weaponMagazines in allWeaponMagazines)
            {
                foreach (var magazine in weaponMagazines.Value)
                {
                    wcAmmoMagazines[magazine.Item2.Item3] = magazine.Item2.Item1;
                }
            }
        }

        private void MakeNormalizedId(MyDefinitionId definitionId, string friendlyType)
        {
            var friendlyTypeLower = friendlyType.ToLowerInvariant();
            var normalizedStringId = definitionId.ToString().Replace(MyObjectBuilderType.LEGACY_TYPE_PREFIX, string.Empty, StringComparison.InvariantCultureIgnoreCase).ToLowerInvariant();
            stringPhysicalItemMap[normalizedStringId] = definitionId;
            if (normalizedStringId != friendlyTypeLower)
            {
                var normalizedFriendlyId = $"{friendlyType}/{definitionId.SubtypeName}".ToLowerInvariant();
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Adding friendly type {normalizedFriendlyId} -> {definition.Id}");
                stringPhysicalItemMap[normalizedFriendlyId] = definitionId;
                if (!friendlyTypeNames.ContainsKey(definitionId.TypeId))
                {
                    friendlyTypeNames.Add(definitionId.TypeId, friendlyType);
                }
                //else
                //{
                //    if (friendlyTypeNames[definition.Id.TypeId] != friendlyType)
                //    {
                //        MyLog.Default.WriteLineAndConsole($"CargoSort: Mismatch: {definition.Id.TypeId} is {friendlyTypeNames[definition.Id.TypeId]}, wants to be {friendlyType}");
                //    }
                //}
            }
        }

        public bool TryGetNormalizedItemDefinition(string shortStringName, out MyDefinitionId definitionId)
        {
            if (stringPhysicalItemMap.TryGetValue(shortStringName, out definitionId))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Normalized type lookup {shortStringName} -> {definitionId}");
                return true;
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Normalized type lookup {shortStringName} failed");
            return false;
        }

        public string GetFriendlyTypeName(MyDefinitionId definitionId)
        {
            string friendlyName;
            if (friendlyTypeNames.TryGetValue(definitionId.TypeId, out friendlyName))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Friendly type lookup {definitionId.TypeId} -> {friendlyName}");
                return friendlyName;
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Friendly type lookup {definitionId.TypeId} failed");
            return definitionId.TypeId.ToString().Replace(MyObjectBuilderType.LEGACY_TYPE_PREFIX, "");
        }

        public string GetFriendlyDefinitionName(MyDefinitionId definitionId)
        {
            return $"{GetFriendlyTypeName(definitionId)}/{definitionId.SubtypeName}";
        }

        public bool TryGetVolume(MyDefinitionId definitionId, out float volume)
        {
            if (allVolumes.TryGetValue(definitionId, out volume))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Volume lookup {definitionId} -> {volume}");
                return true;
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Volume lookup {definitionId} failed");
            return false;
        }

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            if (messageText.StartsWith("/sort", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                var shipController = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyShipController;
                if (shipController == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "You must be seated on a grid to sort!");
                    return;
                }

                BeginSortJob(shipController.CubeGrid, ResultsDisplayType.Chat);
            }
            else
            if (messageText.StartsWith("/getallsortableitems", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                var allSortable = BuildAllSortableItemNamesString();
                if (!string.IsNullOrWhiteSpace(allSortable))
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "All sortable items copied to clipboard!");
                    MyClipboardHelper.SetClipboard(allSortable);
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "No sortable items found to copy to clipboard");
                }
            }
        }

        public void BeginSortJob(IMyCubeGrid rootGrid, ResultsDisplayType resultsDisplayType)
        {
            if (jobTask.valid && !jobTask.IsComplete)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", "A job is already in progress!");
                return;
            }

            var workData = new CargoSorterWorkData(rootGrid, resultsDisplayType);
            jobTask = MyAPIGateway.Parallel.Start(SortInventoryAction, SortInventoryCallback, workData);
        }

        public void BeginQueueNeededJob(IMyShipController shipController)
        {
            if (jobTask.valid && !jobTask.IsComplete)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", "A job is already in progress!");
                return;
            }

            var workData = new CargoSorterWorkData(shipController.CubeGrid);
            jobTask = MyAPIGateway.Parallel.Start(SortInventoryAction, SortInventoryCallback, workData);
        }

        public string GeneratePrerequisiteCustomDataFromQueue(IMyAssembler assembler)
        {
            var efficiencyMultiplier = MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
            var queuePrerequisites = new Dictionary<MyDefinitionId, MyFixedPoint>();
            foreach (var queuedItem in assembler.GetQueue())
            {
                var blueprint = queuedItem.Blueprint as MyBlueprintDefinitionBase;
                if (blueprint == null)
                {
                    continue;
                }
                foreach (var prerequisite in blueprint.Prerequisites)
                {
                    queuePrerequisites[prerequisite.Id] = queuePrerequisites.GetValueOrDefault(prerequisite.Id) + prerequisite.Amount * queuedItem.Amount * (1 / efficiencyMultiplier);
                }
            }

            if (queuePrerequisites.Count > 0)
            {
                return InventoryInfo.BuildCustomData(queuePrerequisites, true);
            }
            else
            {
                return string.Empty;
            }
        }

        public string GenerateResultCustomDataFromQueue(IMyAssembler assembler)
        {
            var efficiencyMultiplier = MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
            var queueResults = new Dictionary<MyDefinitionId, MyFixedPoint>();
            foreach (var queuedItem in assembler.GetQueue())
            {
                var blueprint = queuedItem.Blueprint as MyBlueprintDefinitionBase;
                if (blueprint == null)
                {
                    continue;
                }
                foreach (var result in blueprint.Results)
                {
                    queueResults[result.Id] = queueResults.GetValueOrDefault(result.Id) + result.Amount * queuedItem.Amount;
                }
            }

            if (queueResults.Count > 0)
            {
                return InventoryInfo.BuildCustomData(queueResults, true);
            }
            else
            {
                return string.Empty;
            }
        }

        public bool GenerateQueueFromCustomData(IMyAssembler assembler)
        {
            var result = new ValueTuple<Dictionary<string, MyFixedPoint>, Dictionary<string, MyFixedPoint>>(new Dictionary<string, MyFixedPoint>(), new Dictionary<string, MyFixedPoint>());
            var inputInventory = assembler?.InputInventory as MyInventory;
            if (inputInventory == null)
            {
                return false;
            }
            var inventoryInfo = new InventoryInfo(inputInventory);

            foreach (var request in inventoryInfo.Requests)
            {
                MyBlueprintDefinitionBase blueprintDefinition;
                if (MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(request.Key, out blueprintDefinition))
                {
                    assembler.AddQueueItem(blueprintDefinition, request.Value.Amount);
                }
            }

            return true;
        }

        public string GenerateCustomDataFromProjector(IMyProjector projector)
        {
            ProjectorProxy projectorProxy = new ProjectorProxy(projector);
            if (!projectorProxy.HasBlueprint)
            {
                return string.Empty;
            }
            List<IMySlimBlock> projectedBlocks = new List<IMySlimBlock>();
            projectorProxy.GetBlocks(projectedBlocks);
            var components = new Dictionary<MyDefinitionId, MyFixedPoint>();
            foreach (var projectedBlock in projectedBlocks)
            {
                var blockDef = projectedBlock.BlockDefinition as MyCubeBlockDefinition;
                if (blockDef == null)
                {
                    continue;
                }
                foreach (var component in blockDef.Components)
                {
                    var amount = components.GetValueOrDefault(component.Definition.Id);
                    components[component.Definition.Id] = amount + component.Count;
                }
            }

            if (components.Count > 0)
            {
                return InventoryInfo.BuildCustomData(components, true);
            }
            else
            {
                return string.Empty;
            }
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
                    return c.DisplayNameText?.InsensitiveContains("[nosort]") == false &&
                    c.OtherConnector?.CubeGrid?.CustomName?.InsensitiveContains("[nosort]") == false;
                });

                foreach (var cubeGrid in GridConnectorTree.GatherGrids(nodes))
                {
                    GatherInventory(cubeGrid.GetFatBlocks<IMyTerminalBlock>(), workData);
                }

                workData.Inventories.SortNoAlloc((InventoryInfo x, InventoryInfo y) =>
                {
                    // Blocks and specials go first
                    if (x.TypeRequests.HasFlag(TypeRequests.Special) && !y.TypeRequests.HasFlag(TypeRequests.Special))
                    {
                        return -1;
                    }
                    else if (!x.TypeRequests.HasFlag(TypeRequests.Special) && y.TypeRequests.HasFlag(TypeRequests.Special))
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
                        workData.AvailableForDistribution[item.Key] = workData.AvailableForDistribution.GetValueOrDefault(item.Key) + item.Value;
                    }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Special) || inventoryInfo.TypeRequests.HasFlag(TypeRequests.Limited))
                    {
                        foreach (var request in inventoryInfo.Requests)
                        {
                            // Don't reserve for All containers
                            if (request.Value.Flag == RequestFlags.All)
                            {
                                continue;
                            }

                            workData.AvailableForDistribution[request.Key] = workData.AvailableForDistribution.GetValueOrDefault(request.Key) - request.Value.Amount;
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
                        if (def == null || def.FuelInfos == null || def.FuelInfos.Length != 1)
                        {
                            continue;
                        }

                        var fuelInfo = def.FuelInfos.First();
                        var key = new ValueTuple<TypeRequests, MyDefinitionId>(TypeRequests.ReactorFuel, fuelInfo.FuelId);
                        workData.RequestTypeCount[key] = workData.RequestTypeCount.GetValueOrDefault(key) + 1;

                    }
                    else if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.ConsumableAmmo))
                    {
                        MyDefinitionId wantedAmmo;
                        if (inventoryInfo.Constraint == null)
                        {
                            continue;
                        }

                        // Use the single possibility if there is one
                        if (inventory.Constraint.ConstrainedIds.Count == 1)
                        {
                            wantedAmmo = inventory.Constraint.ConstrainedIds.First();
                        }
                        else
                        {
                            // Try WC since we can have more than 1 ammo!
                            wantedAmmo = GetActiveAmmo(inventoryInfo.Block as MyEntity);
                        }

                        // Ignore weaponcore energy "ammo" or empty ammos which can happen if WC fails
                        if (wantedAmmo == IgnoredEnergyAmmoDefinitionId || wantedAmmo == default(MyDefinitionId))
                        {
                            continue;
                        }

                        var wantedAmount = inventoryInfo.ComputeAmountThatFits(wantedAmmo, true);

                        if (wantedAmount <= MyFixedPoint.Zero || wantedAmount >= MyFixedPoint.MaxValue)
                        {
                            continue;
                        }

                        workData.AvailableForDistribution[wantedAmmo] = workData.AvailableForDistribution.GetValueOrDefault(wantedAmmo) - wantedAmount;
                    }
                }
            }
        }
        private bool IsIgnored(IMyTerminalBlock block)
        {
            if (!HasConveyorSupport(block))
            {
                return true;
            }
            foreach (var item in Instance.Config.LockedContainerKeywords)
            {
                if (block.DisplayNameText.Contains(item))
                {
                    return true;
                }
            }
            return false;
        }

        private void BuildExcessItemPool(CargoSorterWorkData workData)
        {
            foreach (var inventory in workData.Inventories)
            {
                foreach (var item in inventory.VirtualInventory)
                {
                    var excess = -CalculateAmountWanted(inventory, item.Key, item.Value, workData);
                    if (excess <= MyFixedPoint.Zero)
                    {
                        continue;
                    }
                    var pool = workData.ExcessPools.GetValueOrNew(item.Key);
                    pool.Add(new ExcessInfo(inventory, excess));
                }
            }
            // Put the lowest priority inventories first so the highest priority can be popped off the end
            foreach (var pool in workData.ExcessPools)
            {
                pool.Value.Reverse();
            }
        }

        private void BuildExcessItemMovement(CargoSorterWorkData workData)
        {
            //MyLog.Default.WriteLineAndConsole($"CargoSort: Removing excess items");
            List<MyDefinitionId> inventoryKeys = new List<MyDefinitionId>();

            for (int destInvIndex = 0; destInvIndex < workData.Inventories.Count; destInvIndex++)
            {
                var destInventory = workData.Inventories[destInvIndex];
                if (destInventory.TypeRequests.Equals(TypeRequests.Nothing))
                {
                    continue;
                }

                var destPBInv = (VRage.Game.ModAPI.Ingame.IMyInventory)destInventory.RealInventory;
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv destination: {destInventory.Block?.DisplayNameText}");

                foreach (var pool in workData.ExcessPools)
                {
                    var destCurrentAmount = destInventory.VirtualInventory.GetValueOrDefault(pool.Key);
                    var amountWanted = CalculateAmountWanted(destInventory, pool.Key, destCurrentAmount, workData);
                    // We don't want this item or we can't fit any more
                    if (amountWanted <= MyFixedPoint.Zero)
                    {
                        continue;
                    }

                    for (int i = pool.Value.Count - 1; i >= 0; i--)
                    {
                        var inventoryExcess = pool.Value[i];
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: CalculateAmountWanted: Excess AmountToBeMoved");
                        MyFixedPoint amountToBeMoved = MyFixedPoint.Min(amountWanted, inventoryExcess.Amount);
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: amountToBeMoved {pool.Key}: {amountToBeMoved} amountWanted {amountWanted}");
                        if (amountToBeMoved <= MyFixedPoint.Zero)
                        {
                            break;
                        }

                        MyFixedPoint volumeToBeMoved;
                        MyFixedPoint massToBeMoved;
                        if (!destInventory.CanItemsFit(amountToBeMoved, pool.Key, out volumeToBeMoved, out massToBeMoved))
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: Could not add {pool.Key} with amount {amountToBeMoved} to inventory");
                            break;
                        }

                        var sourceInventory = inventoryExcess.Inventory;
                        var sourcePBInv = (VRage.Game.ModAPI.Ingame.IMyInventory)sourceInventory.RealInventory;

                        if (sourceInventory.VirtualInventory.GetValueOrDefault(pool.Key) < amountToBeMoved)
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: Source inventory {sourceInventory.Block?.DisplayNameText} is missing {pool.Key} with amount {amountToBeMoved} to inventory");
                            continue;
                        }

                        if (!sourcePBInv.CanTransferItemTo(destPBInv, pool.Key))
                        {
                            continue;
                        }

                        //MyLog.Default.WriteLineAndConsole($"CargoSort: Inv source: {sourceInventory.Block?.DisplayNameText}");
                        AppendInventoryOperation(workData, new InventoryMovement(sourceInventory, destInventory, pool.Key, amountToBeMoved, volumeToBeMoved, massToBeMoved));
                        // Decrement the excess pool.
                        if (inventoryExcess.Amount <= amountToBeMoved)
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: Pool {pool.Key} source {sourceInventory.Block?.DisplayNameText} empty, removing");
                            pool.Value.RemoveAtFast(i);
                        }
                        else
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: Pool {pool.Key} source {sourceInventory.Block?.DisplayNameText} lost some but not all, now {inventoryExcess.Item2}");
                            inventoryExcess.Amount -= amountToBeMoved;
                            pool.Value[i] = inventoryExcess;
                        }
                        // Recalculate how much is needed and bail out of we don't want any more
                        destCurrentAmount = destInventory.VirtualInventory.GetValueOrDefault(pool.Key);
                        amountWanted = CalculateAmountWanted(destInventory, pool.Key, destCurrentAmount, workData);
                        if (amountWanted <= MyFixedPoint.Zero)
                        {
                            break;
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

                    var sourceSpecial = sourceInventory.TypeRequests.HasFlag(TypeRequests.Special);
                    var destSpecial = destInventory.TypeRequests.HasFlag(TypeRequests.Special);

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
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: CalculateAmountWanted: Desired AmountToBeMoved");
                        MyFixedPoint amountToBeMoved = MyFixedPoint.Min(CalculateAmountWanted(destInventory, virtualItemKey, virtualItemValue, workData), virtualItemValue);
                        if (amountToBeMoved <= MyFixedPoint.Zero || !sourcePBInv.CanTransferItemTo(destPBInv, virtualItemKey))
                        {
                            continue;
                        }

                        //MyLog.Default.WriteLineAndConsole($"CargoSort: amountToBeMoved {virtualItemKey}: {amountToBeMoved}");

                        MyFixedPoint volumeToBeMoved;
                        MyFixedPoint massToBeMoved;
                        if (!destInventory.CanItemsFit(amountToBeMoved, virtualItemKey, out volumeToBeMoved, out massToBeMoved))
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
                    virtualAmount = inventoryInfo.VirtualInventory.GetValueOrDefault(definitionId);

                    // <= 0 disables the feature
                    if (Config.GasGeneratorFillPercent <= 0)
                    {
                        return MyFixedPoint.Zero;
                    }

                    return percentFull < Config.GasGeneratorFillPercent / 2f || percentFull > 1f - ((1f - Config.GasGeneratorFillPercent) / 2f)
                        ? inventoryInfo.ComputeAmountThatCouldFit(definitionId, true,
                        (float)inventoryInfo.MaxVolume * (1f - Config.GasGeneratorFillPercent),
                        (float)inventoryInfo.MaxMass * (1f - Config.GasGeneratorFillPercent)) - virtualAmount
                        : MyFixedPoint.Zero;
                case TypeRequests.AssemblerIngots:
                    var assembler = inventoryInfo.RealInventory?.Entity as IMyAssembler;
                    // Make sure the output inventory is clear in normal operation.
                    if (assembler != null && assembler.IsProducing && assembler.Enabled)
                    {
                        MyInventoryConstraint constraintToCheck = null;
                        switch (assembler.Mode)
                        {
                            case Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly:
                                if (inventoryInfo.RealInventory != assembler.InputInventory)
                                {
                                    // Always clear output side when assembling
                                    return -currentValue;
                                }
                                constraintToCheck = ((MyInventory)assembler.InputInventory)?.Constraint;
                                break;
                            case Sandbox.ModAPI.Ingame.MyAssemblerMode.Disassembly:
                                if (inventoryInfo.RealInventory != assembler.OutputInventory)
                                {
                                    // Always clear input side when disassembling
                                    return -currentValue;
                                }
                                constraintToCheck = ((MyInventory)assembler.OutputInventory)?.Constraint;
                                break;
                        }

                        if (constraintToCheck != null && constraintToCheck.ConstrainedIds.Contains(definitionId))
                        {
                            var efficiencyMultiplier = MyAPIGateway.Session.AssemblerEfficiencyMultiplier;
                            MyFixedPoint newAmount = -currentValue;
                            // Crawl the queue's blueprints to see if what we have is what we need, and get rid of stuff we don't need.
                            foreach (var queuedItem in assembler.GetQueue())
                            {
                                var blueprint = queuedItem.Blueprint as MyBlueprintDefinitionBase;
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
                        }
                    }
                    // If the assembler is off or full somehow, just take everything out.
                    return -currentValue;
                case TypeRequests.RefineryOre:
                    var refinery = inventoryInfo.RealInventory?.Entity as IMyRefinery;
                    if (refinery != null)
                    {
                        var inputConstraint = ((MyInventory)refinery.InputInventory)?.Constraint;
                        if (inventoryInfo.RealInventory == refinery.InputInventory && inputConstraint != null && inputConstraint.ConstrainedIds.Contains(definitionId))
                        {
                            // Only clear the refinery input if the refinery is off
                            return refinery.IsProducing && refinery.Enabled ? MyFixedPoint.Zero : -currentValue;
                        }
                    }
                    // If this is the refinery output, or the refinery is off or full somehow, take everything out.
                    return -currentValue;
                case TypeRequests.GasTankBottles:
                    // Remove all bottles from tanks
                    return -currentValue;
                case TypeRequests.SorterItems:
                    var sorter = inventoryInfo.RealInventory?.Entity as IMyConveyorSorter;
                    return sorter != null && sorter.DrainAll ? MyFixedPoint.Zero : -currentValue;
                case TypeRequests.ReactorFuel:
                    var reactor = inventoryInfo.RealInventory?.Entity as IMyReactor;
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

                    var configuredExpected = reactor.CubeGrid?.GridSizeEnum == MyCubeSize.Large ? Config.ExpectedLargeGridReactorFuel : Config.ExpectedSmallGridReactorFuel;

                    // <= 0 disables the feature
                    if (configuredExpected <= 0)
                    {
                        return MyFixedPoint.Zero;
                    }

                    var expectedAmount = (MyFixedPoint)Math.Min(
                         (float)availableForDistribution / (float)typeRequestCount,
                         ((float)configuredExpected * reactor.PowerOutputMultiplier)
                        );
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel {inventoryInfo.Block?.DisplayNameText} expectedAmount {expectedAmount}");
                    virtualAmount = inventoryInfo.VirtualInventory.GetValueOrDefault(definitionId);

                    if (virtualAmount < expectedAmount * 0.5f)
                    {
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel too little, returning ({expectedAmount} - {virtualAmount}) {expectedAmount - virtualAmount}");
                        return expectedAmount - virtualAmount;
                    }
                    else if (currentValue > expectedAmount)
                    {
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel too much, returning ({expectedAmount} - {currentValue}) {expectedAmount - currentValue}");
                        return expectedAmount - currentValue;
                    }
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: ReactorFuel in range, returning 0 wanted");
                    return MyFixedPoint.Zero;
                case TypeRequests.ConsumableAmmo:
                    return inventoryInfo.ComputeAmountThatFits(definitionId);
                case TypeRequests.Special:
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Special request amount {definitionId} {GetSpecialRequestAmount(inventoryInfo, definitionId, currentValue)}");
                    return GetRequestAmount(inventoryInfo, definitionId, currentValue);
                default:
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Limited) && inventoryInfo.Requests.ContainsKey(definitionId))
                    {
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: Limited request amount {definitionId} {GetSpecialRequestAmount(inventoryInfo, definitionId, currentValue)}");
                        return GetRequestAmount(inventoryInfo, definitionId, currentValue);
                    }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ores) && allOres.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ingots) && allIngots.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Components) && allComponents.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Ammo) && allAmmo.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Tools) && allTools.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    if (inventoryInfo.TypeRequests.HasFlag(TypeRequests.Bottles) && allBottles.Contains(definitionId)) { return inventoryInfo.ComputeAmountThatFits(definitionId); }
                    return -currentValue;
            }
        }

        private static MyFixedPoint GetRequestAmount(InventoryInfo inventoryInfo, MyDefinitionId definitionId, MyFixedPoint currentValue)
        {
            RequestData requestInfo;
            if (inventoryInfo.Requests.TryGetValue(definitionId, out requestInfo))
            {
                MyFixedPoint virtualAmount;
                virtualAmount = inventoryInfo.VirtualInventory.GetValueOrDefault(definitionId);

                if ((requestInfo.Flag <= RequestFlags.All) ||
                    (requestInfo.Flag == RequestFlags.Limit && virtualAmount > requestInfo.Amount) ||
                    (requestInfo.Flag == RequestFlags.Minimum && virtualAmount < requestInfo.Amount))
                {
                    return MyFixedPoint.Min(inventoryInfo.ComputeAmountThatFits(definitionId, true), requestInfo.Amount - virtualAmount);
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
            MyFixedPoint destChangedAmount = operation.Destination.VirtualInventory.GetValueOrDefault(operation.Item) + operation.Amount;
            operation.Destination.VirtualInventory[operation.Item] = destChangedAmount;
            workData.MovementData.Add(operation);
        }

        private void SortInventoryCallback(WorkData data)
        {
            jobTask = new Task();
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

            var transferRequestCount = ExecuteMovementData(workData);
            DisplayResults(workData, transferRequestCount);
        }

        private void DisplayResults(CargoSorterWorkData workData, int transferRequestCount)
        {
            var validationFailedBlocks = new Dictionary<IMyCubeBlock, RequestValidationStatus>();
            foreach (var inventory in workData.Inventories)
            {
                if (inventory.RequestStatus == RequestValidationStatus.Valid || !Util.IsValid(inventory.Block))
                {
                    continue;
                }

                validationFailedBlocks[inventory.Block] = inventory.RequestStatus;
            }

            switch (workData.ResultsType)
            {
                case ResultsDisplayType.Chat:
                    if (Config.ShowProgressNotifications)
                    {
                        if (transferRequestCount == 0)
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", "No transfers needed.");
                        }
                        else
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"{transferRequestCount} transfers requested.");
                        }
                    }

                    foreach (var failedBlock in validationFailedBlocks)
                    {
                        if (failedBlock.Value.HasFlag(RequestValidationStatus.TooMuchVolume))
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"Warning: Requested items on '{failedBlock.Key.DisplayNameText}' will not fit!");
                        }
                        if (failedBlock.Value.HasFlag(RequestValidationStatus.InvalidCustomData))
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"Invalid Custom Data on container '{failedBlock.Key.DisplayNameText}'");
                        }
                        else if (failedBlock.Value.HasFlag(RequestValidationStatus.InvalidItem) || failedBlock.Value.HasFlag(RequestValidationStatus.InvalidCount))
                        {
                            var ini = new MyIni();
                            var terminalBlock = failedBlock.Key as IMyTerminalBlock;
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
                                if (!TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                                {
                                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Unknown item '{iniKey.Name}' in Custom Data on container '{terminalBlock.DisplayNameText}'");
                                    continue;
                                }

                                var value = ini.Get(iniKey);
                                var valueString = value.ToString();
                                if (string.IsNullOrWhiteSpace(valueString) || valueString.Equals("All", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    continue;
                                }

                                int itemCount;
                                if (!int.TryParse(valueString.TrimEnd('l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                                {
                                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Invalid count '{valueString}' for type '{iniKey.Name}' in Custom Data on container '{terminalBlock.DisplayNameText}'");
                                    continue;
                                }
                            }
                        }
                    }

                    if (Config.ShowMissingItems)
                    {
                        foreach (var availability in workData.AvailableForDistribution.OrderByDescending(kv => (float)kv.Value))
                        {
                            if (availability.Value >= MyFixedPoint.Zero)
                            {
                                continue;
                            }
                            MyAPIGateway.Utilities.ShowMessage("Needed", $"{MyFixedPoint.Ceiling(-availability.Value)} {GetFriendlyDefinitionName(availability.Key)}");
                        }
                    }
                    break;
                case ResultsDisplayType.Window:
                    var groups = new Dictionary<string, Dictionary<string, MyFixedPoint>>();
                    StringBuilder warningsBuilder = null;
                    if (validationFailedBlocks.Count > 0)
                    {
                        warningsBuilder = new StringBuilder();

                        foreach (var failedBlock in validationFailedBlocks)
                        {
                            warningsBuilder.AppendFormat("{0}:\n", failedBlock.Key.DisplayNameText);

                            if (failedBlock.Value.HasFlag(RequestValidationStatus.TooMuchVolume))
                            {
                                warningsBuilder.AppendLine("The block's Custom Data requests more items than can possibly fit in its inventory. Reduce the number of items desired or move the Custom Data, tag and priority to a block with more inventory space.");
                            }
                            if (failedBlock.Value.HasFlag(RequestValidationStatus.InvalidCustomData))
                            {
                                warningsBuilder.AppendLine("The block's Custom Data was not able to be interpreted as an inventory request. Clear the block's Custom Data and set it up again or remove the Limited/Special tag.");
                            }
                            else if (failedBlock.Value.HasFlag(RequestValidationStatus.InvalidItem) || failedBlock.Value.HasFlag(RequestValidationStatus.InvalidCount))
                            {
                                warningsBuilder.AppendLine("These lines in the block's Custom Data are not valid:");
                                var ini = new MyIni();
                                var terminalBlock = failedBlock.Key as IMyTerminalBlock;
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
                                    if (!TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                                    {
                                        warningsBuilder.AppendFormat("Unknown item: {0}", iniKey.Name).AppendLine();
                                        continue;
                                    }

                                    var value = ini.Get(iniKey);
                                    var valueString = value.ToString();
                                    if (string.IsNullOrWhiteSpace(valueString) || valueString.Equals("All", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        continue;
                                    }

                                    int itemCount;
                                    if (!int.TryParse(valueString.TrimEnd('l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                                    {
                                        warningsBuilder.AppendFormat("Invalid requested value: '{0}' for type '{1}'", valueString, iniKey.Name).AppendLine();
                                        continue;
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
                        string friendlyName = GetFriendlyTypeName(availability.Key);
                        var group = groups.GetValueOrNew(friendlyName);
                        group[availability.Key.SubtypeName] = group.GetValueOrDefault(availability.Key.SubtypeName) + availability.Value;
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
                                    displayStringBuilder.AppendFormat("{0}: {1}\n", subTypeValue.Key, MyFixedPoint.Ceiling(-subTypeValue.Value));
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
                        if (Config.CopyResultsToClipboard && groups.Count > 0 && clickResult == ResultEnum.OK)
                        {
                            var clipboardStringBuilder = new StringBuilder();
                            foreach (var group in groups.OrderBy(g => g.Key))
                            {
                                foreach (var subTypeValue in group.Value.OrderBy(g => (float)g.Value))
                                {
                                    clipboardStringBuilder.AppendFormat("{0},{1},{2}\n", group.Key, subTypeValue.Key, MyFixedPoint.Ceiling(-subTypeValue.Value));
                                }
                            }
                            MyClipboardHelper.SetClipboard(clipboardStringBuilder.ToString());
                        }
                    }, Config.CopyResultsToClipboard && groups.Count > 0 ? "Copy to Clipboard" : null);

                    break;
                default:
                    break;
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

        private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (block is IMyShipController)
            {
                actions.Add(TerminalControls.SortAction);
            }
        }

        private void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block is IMyShipController)
            {
                controls.AddOrInsert(TerminalControls.SortButton, 5);
            }
            else if (block is IMyAssembler)
            {
                controls.Add(TerminalControls.GeneratePrerequisiteCustomDataFromQueue);
                controls.Add(TerminalControls.GenerateResultCustomDataFromQueue);
                controls.Add(TerminalControls.GenerateQueueFromCustomData);
            }
            else if (block is IMyProjector)
            {
                controls.Add(TerminalControls.GenerateCustomDataFromProjection);
            }
        }

        private string BuildAllSortableItemNamesString()
        {
            var sbMap = new StringBuilder();
            var sbInv = new StringBuilder();
            Dictionary<MyDefinitionId, MyFixedPoint> itemIdsForCustomData = new Dictionary<MyDefinitionId, MyFixedPoint>();
            sbMap.AppendLine("Sortable Items:");
            sbMap.AppendLine("Sortable ID = Display Name");
            foreach (var item in MakeSortedIdDefs(allOres))
            {
                sbMap.AppendFormat("{0} = {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }
            foreach (var item in MakeSortedIdDefs(allIngots))
            {
                sbMap.AppendFormat("{0} = {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }
            foreach (var item in MakeSortedIdDefs(allComponents))
            {
                sbMap.AppendFormat("{0} = {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }
            foreach (var item in MakeSortedIdDefs(allAmmo))
            {
                sbMap.AppendFormat("{0} = {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }
            foreach (var item in MakeSortedIdDefs(allTools))
            {
                sbMap.AppendFormat("{0} = {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }
            foreach (var item in MakeSortedIdDefs(allBottles))
            {
                sbMap.AppendFormat("{0} = {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            sbMap.AppendLine().AppendLine("Inventory Custom Data format:");
            sbMap.AppendLine("[Inventory]");
            sbMap.AppendStringBuilder(sbInv);
            return sbMap.ToString();
        }

        private IOrderedEnumerable<KeyValuePair<string, string>> MakeSortedIdDefs(IEnumerable<MyDefinitionId> defs)
        {
            return defs.Select(i =>
            {
                var def = MyDefinitionManager.Static.GetDefinition(i);
                if (def != null)
                {
                    return new KeyValuePair<string, string>(GetFriendlyDefinitionName(i), def.DisplayNameText);
                }
                return default(KeyValuePair<string, string>);
            }).Where(i => !string.IsNullOrEmpty(i.Key)).OrderBy(i => i.Key);
        }

        protected override void UnloadData()
        {
            if (Util.IsDedicatedServer)
            {
                return;
            }
            if (wcApi.IsReady)
            {
                wcApi.Unload();
            }
            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter -= CustomActionGetter;
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            Instance = null;
        }

        // Copied in part from BuildVision. Thanks Digi!
        public static bool HasConveyorSupport(IMyCubeBlock block)
        {
            if (!Util.IsValid(block))
            {
                return false;
            }

            bool supportsConveyors;
            if (blockConveyorSupport.TryGetValue(block.BlockDefinition, out supportsConveyors))
            {
                return supportsConveyors;
            }

            var dummies = new Dictionary<string, IMyModelDummy>();
            block.Model.GetDummies(dummies);

            foreach (var dummy in dummies)
            {
                if (dummy.Value.Name.StartsWith("detector_conveyor", StringComparison.InvariantCultureIgnoreCase))
                {
                    supportsConveyors = true;
                    break;
                }
            }

            blockConveyorSupport.Add(block.BlockDefinition, supportsConveyors);
            return supportsConveyors;
        }

        // Required because many weaponcore weapons are conveyor sorters
        public bool IsSorter(IMyConveyorSorter myConveyorSorter)
        {
            return sorters.Contains(myConveyorSorter.BlockDefinition);
        }

        // Get the active ammo for a WC weapon
        public MyDefinitionId GetActiveAmmo(MyEntity weapon, int weaponId = 0)
        {
            if (!Util.IsValid(weapon) || wcAmmoMagazines.Count == 0 || !wcApi.IsReady)
            {
                return default(MyDefinitionId);
            }
            var activeAmmo = wcApi.GetActiveAmmo(weapon, weaponId);
            if (string.IsNullOrEmpty(activeAmmo))
            {
                return default(MyDefinitionId);
            }
            return wcAmmoMagazines.GetValueOrDefault(activeAmmo);
        }
    }
}