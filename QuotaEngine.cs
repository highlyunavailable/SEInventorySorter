using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using InventorySorter.VirtualInventory;
using ParallelTasks;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;

namespace InventorySorter
{
    public static class QuotaEngine
    {
        // Entry point — called from CargoSorterSessionComponent.BeginQuotaJob
        public static void Run(WorkData data)
        {
            var workData = (QuotaManagerWorkData)data;
            try
            {
                var tree = new GridConnectorTree(workData.Block.CubeGrid);
                var nodes = tree.GatherRecursive(c => c.CustomName?.InsensitiveContains("[nosort]") == false &&
                                                      c.OtherConnector?.CustomName?.InsensitiveContains("[nosort]") == false &&
                                                      c.OtherConnector?.CubeGrid?.CustomName?.InsensitiveContains("[nosort]") == false);

                foreach (var cubeGrid in GridConnectorTree.GatherGrids(nodes))
                {
                    GatherQuotaAndAssemblers(cubeGrid.GetFatBlocks<IMyTerminalBlock>(), workData);
                }

                TrimUnhandledItems(workData);

                // MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Item quota differences:");
                // foreach (var item in workData.MissingItems)
                // {
                //     if (item.Value > 0)
                //     {
                //         MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Missing: {item.Value}");
                //     }
                //     else
                //     {
                //         MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Excess: {-item.Value}");
                //     }
                // }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole($"CargoSort: Quota management failed with exception:\n{ex}");
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Internal error: {ex.Message}");
            }
        }

        // Callback — called from CargoSorterSessionComponent.SetProductionQuotasCallback
        public static void OnComplete(WorkData data)
        {
            var workData = (QuotaManagerWorkData)data;
            CargoSorterSessionComponent.Instance.JobTask = new Task();
            ExecuteQueueChanges(workData);
            DisplayQuotaResults(workData);
        }

        private static void TrimUnhandledItems(QuotaManagerWorkData workData)
        {
            // Trim any items that are missing that can't be handled by any assembler
            foreach (var item in workData.MissingItems)
            {
                // Nothing to do with this item, we have exactly enough
                if (item.Value == 0)
                {
                    // MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Trimming {item.Key} 0 items");
                    workData.ItemAvailableAssemblers.Add(item.Key, null);
                    continue;
                }

                bool itemSatisfied = false;
                // Check to see if there any disassemblers that can handle this item
                if (item.Value < 0 && !workData.ActiveAssembling.Contains(item.Key))
                {
                    foreach (var quotaItem in workData.QuotaInfo.QuotaItems)
                    {
                        if (quotaItem.ItemId != item.Key)
                        {
                            continue;
                        }

                        // MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: data {quotaItem.ItemId} {quotaItem.Amount} - {item.Value.Abs()} <= {quotaItem.Deviation}");
                        if (item.Value.Abs() <= quotaItem.Deviation)
                        {
                            // MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Skipping disassembly for {quotaItem.ItemId} - {item.Value.Abs()} <= {quotaItem.Deviation}");
                            workData.ItemAvailableAssemblers.Add(item.Key, null);
                            itemSatisfied = true;
                        }

                        break;
                    }

                    if (itemSatisfied)
                    {
                        continue;
                    }

                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Looking for disassemblers for {item.Key}");
                    foreach (var assembler in workData.GroupAssemblers)
                    {
                        if (assembler.AllowDisassembly)
                        {
                            //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Found disassembler {assembler.Block.CustomName}");
                            List<MyBlueprintDefinitionBase> blueprintDefinitions;
                            if (CargoSorterSessionComponent.Instance.TryGetBlueprintDefinitionsByResultId(item.Key, out blueprintDefinitions))
                            {
                                // Find usable blueprint
                                foreach (var blueprint in blueprintDefinitions)
                                {
                                    if (assembler.Block.CanUseBlueprint(blueprint))
                                    {
                                        var availableAssemblers = workData.ItemAvailableAssemblers.GetValueOrDefault(item.Key, new List<AssemblerQuotaInfo>());
                                        availableAssemblers.Add(assembler);
                                        workData.MarkedForDisassembly.Add(assembler.Block);
                                        workData.ItemAvailableAssemblers[item.Key] = availableAssemblers;
                                        itemSatisfied = true;
                                        //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Marking {assembler.Block.CustomName} for disassembly");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                else if (item.Value > 0 && !workData.ActiveDisassembling.Contains(item.Key))
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Looking for assemblers for {item.Key}");
                    foreach (var assembler in workData.GroupAssemblers)
                    {
                        if (assembler.AllowAssembly && !workData.MarkedForDisassembly.Contains(assembler.Block))
                        {
                            List<MyBlueprintDefinitionBase> blueprintDefinitions;
                            if (CargoSorterSessionComponent.Instance.TryGetBlueprintDefinitionsByResultId(item.Key, out blueprintDefinitions))
                            {
                                // Find usable blueprint
                                foreach (var blueprint in blueprintDefinitions)
                                {
                                    if (assembler.Block.CanUseBlueprint(blueprint))
                                    {
                                        var availableAssemblers = workData.ItemAvailableAssemblers.GetValueOrDefault(item.Key, new List<AssemblerQuotaInfo>());
                                        availableAssemblers.Add(assembler);
                                        workData.ItemAvailableAssemblers[item.Key] = availableAssemblers;
                                        itemSatisfied = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (!itemSatisfied)
                {
                    workData.ItemAvailableAssemblers.Add(item.Key, null);
                }
            }

            // If there is no available weight that means we can't handle the item, trim it
            foreach (var item in workData.ItemAvailableAssemblers)
            {
                if (item.Value == null || item.Value.Count == 0)
                {
                    var missingCount = workData.MissingItems.GetValueOrDefault(item.Key);
                    var removeMissing = missingCount == 0;
                    if (!removeMissing)
                    {
                        foreach (var quotaItem in workData.QuotaInfo.QuotaItems)
                        {
                            if (quotaItem.ItemId != item.Key)
                            {
                                continue;
                            }

                            if (missingCount.Abs() > quotaItem.Deviation)
                            {
                                // MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: {quotaItem.ItemId} is out of range, keeping {missingCount.Abs()} > {quotaItem.Deviation}");
                                continue;
                            }

                            removeMissing = true;
                            break;
                        }
                    }

                    if (removeMissing)
                    {
                        //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Trimming {item.Key} from MissingItems");
                        workData.MissingItems.Remove(item.Key);
                    }

                    continue;
                }

                // Reorder the assemblers as this is used later to handle assembly/disassembly priority
                item.Value.SortNoAlloc((x, y) =>
                {
                    // Sort assemblers that clear their queue first
                    var comparedClear = y.ClearQueue.CompareTo(x.ClearQueue);
                    if (comparedClear == 0)
                    {
                        // Sort assemblers that have a higher weight first
                        return y.AssemblerWeight.CompareTo(x.AssemblerWeight);
                    }

                    return comparedClear;
                });
            }
        }

        private static void GatherQuotaAndAssemblers(IEnumerable<IMyTerminalBlock> blocks, QuotaManagerWorkData workData)
        {
            //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: getting all assemblers for assembler group {workData.QuotaInfo.GroupName}");
            foreach (var block in blocks)
            {
                if (!Util.IsValid(block) || block.InventoryCount == 0 || !block.HasLocalPlayerAccess() || CargoSorterSessionComponent.Instance.IsIgnored(block))
                {
                    continue;
                }

                var gatherInventoryContents = false;
                var assembler = block as IMyAssembler;
                if (assembler != null)
                {
                    if (string.IsNullOrWhiteSpace(workData.QuotaInfo.GroupName) ? workData.Block == block : assembler.CustomName.InsensitiveContains(workData.QuotaInfo.GroupName))
                    {
                        var parseResult = ProductionQuotaInfo.Parse(assembler);

                        if (parseResult.Success)
                        {
                            workData.GroupAssemblers.Add(ProductionQuotaInfo.ReadOptions(assembler));
                            if (assembler == workData.Block) // Read primary assembler quota data
                            {
                                workData.QuotaInfo.ConfigParseResult = parseResult;
                                ProductionQuotaInfo.ReadQuota(assembler, workData.QuotaInfo);
                            }
                        }

                        if (!assembler.IsQueueEmpty)
                        {
                            foreach (var queuedItem in assembler.GetQueue())
                            {
                                var blueprint = queuedItem.Blueprint as MyBlueprintDefinitionBase;
                                if (blueprint == null)
                                {
                                    continue;
                                }

                                foreach (var result in blueprint.Results)
                                {
                                    if (assembler.Mode == Sandbox.ModAPI.Ingame.MyAssemblerMode.Disassembly)
                                    {
                                        workData.ActiveDisassembling.Add(result.Id);
                                    }
                                    else
                                    {
                                        workData.ActiveAssembling.Add(result.Id);
                                    }
                                }
                            }
                        }

                        gatherInventoryContents = true;
                    }

                    // Add all coop assembler inventories as they'll pull stuff anyway.
                    if (assembler.CooperativeMode)
                    {
                        gatherInventoryContents = true;
                    }
                }
                else
                {
                    gatherInventoryContents = block.CustomName.InsensitiveContains(CargoSorterSessionComponent.Instance.Config.QuotaContainerKeyword);
                }

                if (!gatherInventoryContents)
                {
                    continue;
                }

                for (int i = 0; i < block.InventoryCount; i++)
                {
                    var inventory = block.GetInventory(i) as MyInventory;
                    foreach (var item in inventory.GetItems())
                    {
                        MyFixedPoint amount;
                        if (!workData.MissingItems.TryGetValue(item.Content.GetId(), out amount))
                        {
                            continue;
                        }

                        amount -= item.Amount;
                        workData.MissingItems[item.Content.GetId()] = amount;
                    }
                }
            }

            foreach (var item in workData.QuotaInfo.QuotaItems)
            {
                workData.MissingItems[item.ItemId] = item.Amount;
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Found {workData.GroupAssemblers.Count} assemblers for {workData.QuotaInfo.GroupName}");
        }

        private static void ExecuteQueueChanges(QuotaManagerWorkData workData)
        {
            if (workData.QuotaInfo.QuotaItems.Count == 0)
            {
                return;
            }

            var availableAssemblers = new List<AssemblerQuotaInfo>();

            // Iterate by QuotaItems so the priority order is preserved
            // Reversed so that we can add to the first index every time and push other items back in queue
            // and the highest priority is done last and therefore ends up being first.
            for (int qi = workData.QuotaInfo.QuotaItems.Count - 1; qi >= 0; qi--)
            {
                var quotaItem = workData.QuotaInfo.QuotaItems[qi];
                var missingItemCount = workData.MissingItems.GetValueOrDefault(quotaItem.ItemId);
                var disassembling = missingItemCount < MyFixedPoint.Zero;
                var remainingToQueue = MyFixedPoint.Floor(disassembling ? (-missingItemCount) - quotaItem.Deviation : missingItemCount);

                //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Starting Remaining To Queue: {remainingToQueue}");

                if (remainingToQueue == MyFixedPoint.Zero)
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Skipping {quotaItem.ItemId} - 0 items");
                    continue;
                }

                var itemAssemblers = workData.ItemAvailableAssemblers.GetValueOrDefault(quotaItem.ItemId, null);
                if (itemAssemblers == null)
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Skipping {quotaItem.ItemId} - no available assemblers");
                    continue;
                }

                List<MyBlueprintDefinitionBase> blueprints;
                if (!CargoSorterSessionComponent.Instance.TryGetBlueprintDefinitionsByResultId(quotaItem.ItemId, out blueprints) || blueprints.Count == 0)
                {
                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: Skipping {quotaItem.ItemId} - no blueprint");
                    continue;
                }

                if (disassembling)
                {
                    workData.MissingItems[quotaItem.ItemId] += quotaItem.Deviation;
                }

                availableAssemblers.Clear();
                availableAssemblers.AddRange(itemAssemblers);

                float totalWeight = 0;
                for (int ai = availableAssemblers.Count - 1; ai >= 0; ai--)
                {
                    var assembler = availableAssemblers[ai];
                    if (workData.MarkedForDisassembly.Contains(assembler.Block))
                    {
                        // This assembler is for disassembly, and therefore cannot be used to assemble.
                        if (!disassembling)
                        {
                            //MyLog.Default.WriteLineAndConsole($"Assembler {assembler.Block.CustomName} is disassembling, skipping for assembly item {quotaItem.ItemId} which needs {remainingToQueue} items");
                            availableAssemblers.RemoveAtFast(ai);
                            continue;
                        }
                    }
                    else
                    {
                        // This assembler is for assembly, and therefore cannot be used to disassemble.
                        if (disassembling)
                        {
                            //MyLog.Default.WriteLineAndConsole($"Assembler {assembler.Block.CustomName} is assembling, skipping for disassembly item {quotaItem.ItemId} which needs {remainingToQueue} items");
                            availableAssemblers.RemoveAtFast(ai);
                            continue;
                        }
                    }

                    // Find usable blueprint
                    MyBlueprintDefinitionBase blueprintDefinition = null;
                    if (Util.IsValid(assembler.Block))
                    {
                        foreach (var blueprint in blueprints)
                        {
                            if (assembler.Block.CanUseBlueprint(blueprint))
                            {
                                blueprintDefinition = blueprint;
                                break;
                            }
                        }
                    }

                    // Last chance to skip if it's been destroyed or something
                    if (blueprintDefinition != null)
                    {
                        totalWeight += assembler.AssemblerWeight;
                        if (assembler.Block.IsQueueEmpty)
                        {
                            continue;
                        }

                        var queue = assembler.Block.GetQueue();
                        for (int i = queue.Count - 1; i >= 0; i--)
                        {
                            var queueItem = queue[i];
                            var queuedBlueprint = queueItem.Blueprint as MyBlueprintDefinitionBase;
                            if (queuedBlueprint == blueprintDefinition)
                            {
                                remainingToQueue -= MyFixedPoint.Min(queueItem.Amount, remainingToQueue);
                            }
                        }
                    }
                    else
                    {
                        availableAssemblers.RemoveAtFast(ai);
                    }
                }

                // Out of items to queue, go to next item
                if (remainingToQueue == MyFixedPoint.Zero)
                {
                    continue;
                }

                foreach (var assembler in availableAssemblers)
                {
                    if (workData.MarkedForDisassembly.Contains(assembler.Block))
                    {
                        if (assembler.Block.Mode != Sandbox.ModAPI.Ingame.MyAssemblerMode.Disassembly)
                        {
                            assembler.Block.Mode = Sandbox.ModAPI.Ingame.MyAssemblerMode.Disassembly;
                        }
                    }
                    else
                    {
                        if (assembler.Block.Mode != Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly)
                        {
                            assembler.Block.Mode = Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly;
                        }
                    }

                    // Find usable blueprint
                    MyBlueprintDefinitionBase blueprintDefinition = null;
                    foreach (var blueprint in blueprints)
                    {
                        if (assembler.Block.CanUseBlueprint(blueprint))
                        {
                            blueprintDefinition = blueprint;
                            break;
                        }
                    }

                    //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: remaining: {(remainingToQueue >= MyFixedPoint.Zero ? remainingToQueue : -remainingToQueue)} Weighted: {MyFixedPoint.Ceiling(assembler.AssemblerWeight / totalWeight * (missingItemCount >= MyFixedPoint.Zero ? missingItemCount : -missingItemCount))}");
                    var weightedPortion = MyFixedPoint.Ceiling(assembler.AssemblerWeight / totalWeight * (disassembling ? -missingItemCount : missingItemCount));
                    var assignedAmount = MyFixedPoint.Min(remainingToQueue, weightedPortion);

                    if (assignedAmount == MyFixedPoint.Zero)
                    {
                        continue;
                    }

                    // Disable co-op mode when queueing quotas. This will only disable coop mode on assemblers that are a part of a group.
                    if (assembler.Block.Mode == Sandbox.ModAPI.Ingame.MyAssemblerMode.Assembly && assembler.Block.CooperativeMode)
                    {
                        assembler.Block.CooperativeMode = false;
                    }

                    // Validity checked from the check during total weight determination
                    if (!assembler.Block.IsQueueEmpty)
                    {
                        var queue = assembler.Block.GetQueue();
                        for (int i = queue.Count - 1; i >= 0; i--)
                        {
                            var queueItem = queue[i];
                            var queuedBlueprint = queueItem.Blueprint as MyBlueprintDefinitionBase;
                            if (queuedBlueprint == blueprintDefinition)
                            {
                                assignedAmount -= MyFixedPoint.Min(queueItem.Amount, assignedAmount);
                            }
                            else
                            {
                                if (!assembler.ClearQueue)
                                {
                                    continue;
                                }

                                var hasAnyItem = false;
                                foreach (var result in queuedBlueprint.Results)
                                {
                                    if (!workData.MissingItems.ContainsKey(result.Id))
                                    {
                                        continue;
                                    }

                                    hasAnyItem = true;
                                    break;
                                }

                                if (!hasAnyItem)
                                {
                                    assembler.Block.RemoveQueueItem(i, queueItem.Amount);
                                }
                            }
                        }
                    }

                    if (assignedAmount > MyFixedPoint.Zero)
                    {
                        assembler.Block.InsertQueueItem(0, blueprintDefinition, assignedAmount);
                        remainingToQueue -= assignedAmount;
                    }

                    // Out of items to queue, go to next item
                    if (remainingToQueue == MyFixedPoint.Zero)
                    {
                        break;
                    }
                }
            }
        }

        private static void DisplayQuotaResults(QuotaManagerWorkData workData)
        {
            switch (workData.ResultsType)
            {
                case ResultsDisplayType.Chat:
                    if (CargoSorterSessionComponent.Instance.Config.ShowProgressNotifications && workData.QuotaInfo.RequestStatus == RequestValidationStatus.Valid)
                    {
                        if (workData.MissingItems.Count == 0)
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", "No quota changes needed.");
                        }
                        else
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"{workData.MissingItems.Count} quota changes requested.");
                        }
                    }

                    if (workData.QuotaInfo.RequestStatus != RequestValidationStatus.Valid)
                    {
                        if (workData.QuotaInfo.RequestStatus.HasFlag(RequestValidationStatus.InvalidCustomData))
                        {
                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"Invalid Custom Data on assembler '{workData.Block.CustomName}': {workData.QuotaInfo.ConfigParseResult.Error}");
                        }

                        else if (workData.QuotaInfo.RequestStatus.HasFlag(RequestValidationStatus.InvalidItem) || workData.QuotaInfo.RequestStatus.HasFlag(RequestValidationStatus.InvalidCount))
                        {
                            var ini = new MyIni();
                            var terminalBlock = workData.Block as IMyTerminalBlock;
                            if (Util.IsValid(terminalBlock) && ini.TryParse(terminalBlock.CustomData))
                            {
                                if (ini.ContainsSection(ProductionQuotaInfo.QuotaSectionName))
                                {
                                    var iniKeys = new List<MyIniKey>();
                                    ini.GetKeys(ProductionQuotaInfo.QuotaSectionName, iniKeys);

                                    foreach (var iniKey in iniKeys)
                                    {
                                        if (iniKey.IsEmpty)
                                        {
                                            continue;
                                        }

                                        MyDefinitionId definitionId;
                                        if (!CargoSorterSessionComponent.Instance.TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
                                        {
                                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"Unknown item '{iniKey.Name}' in Custom Data on assembler '{terminalBlock.CustomName}'");
                                            continue;
                                        }

                                        var value = ini.Get(iniKey);
                                        var valueString = value.ToString();
                                        int itemCount;
                                        if (!int.TryParse(valueString.TrimEnd('%', 'l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                                        {
                                            MyAPIGateway.Utilities.ShowMessage("Sorter", $"Invalid count '{valueString}' for type '{iniKey.Name}' in Custom Data on assembler '{terminalBlock.CustomName}'");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    break;
                case ResultsDisplayType.Window:
                    var groups = new Dictionary<string, Dictionary<MyDefinitionId, MyFixedPoint>>();
                    StringBuilder warningsBuilder = null;

                    if (workData.QuotaInfo.RequestStatus != RequestValidationStatus.Valid)
                    {
                        warningsBuilder = new StringBuilder();

                        warningsBuilder.AppendFormat("{0}:\n", workData.Block.CustomName);

                        if (workData.QuotaInfo.RequestStatus.HasFlag(RequestValidationStatus.InvalidCustomData))
                        {
                            warningsBuilder.AppendLine($"The block's Custom Data was not able to be interpreted as a quota request: {workData.QuotaInfo.ConfigParseResult.Error}");
                        }
                        else if (workData.QuotaInfo.RequestStatus.HasFlag(RequestValidationStatus.InvalidItem) || workData.QuotaInfo.RequestStatus.HasFlag(RequestValidationStatus.InvalidCount))
                        {
                            warningsBuilder.AppendLine("These lines in the block's Custom Data are not valid:");
                            var ini = new MyIni();
                            var terminalBlock = workData.Block as IMyTerminalBlock;
                            if (Util.IsValid(terminalBlock) && ini.TryParse(terminalBlock.CustomData))
                            {
                                if (ini.ContainsSection(ProductionQuotaInfo.QuotaSectionName))
                                {
                                    List<MyIniKey> iniKeys = new List<MyIniKey>();
                                    ini.GetKeys(ProductionQuotaInfo.QuotaSectionName, iniKeys);

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

                                        var value = ini.Get(iniKey);
                                        var valueString = value.ToString();
                                        var rangeIndex = valueString.IndexOf('-');
                                        if (rangeIndex != -1)
                                        {
                                            int min;
                                            int max;
                                            if (!int.TryParse(valueString.Substring(0, rangeIndex), out min) || min < 0 || !int.TryParse(valueString.Substring(rangeIndex + 1), out max) || max < min)
                                            {
                                                warningsBuilder.AppendFormat("Invalid range: '{0}' for type '{1}'. Check that minimum is less than maximum and that both are fully numeric.", valueString, iniKey.Name).AppendLine();
                                            }
                                        }
                                        else
                                        {
                                            int itemCount;
                                            if (!int.TryParse(valueString.TrimEnd('%', 'l', 'L', 'm', 'M'), out itemCount) || itemCount < 0)
                                            {
                                                warningsBuilder.AppendFormat("Invalid quota value: '{0}' for type '{1}'", valueString, iniKey.Name).AppendLine();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    foreach (var item in workData.ItemAvailableAssemblers)
                    {
                        if (item.Value == null || item.Value.Count == 0)
                        {
                            var missingCount = workData.MissingItems.GetValueOrDefault(item.Key);
                            if (missingCount == 0)
                            {
                                continue;
                            }

                            if (warningsBuilder == null)
                            {
                                warningsBuilder = new StringBuilder();
                            }

                            var itemName = CargoSorterSessionComponent.Instance.GetFriendlyDefinitionDisplayName(item.Key);
                            if (missingCount > 0 && workData.ActiveDisassembling.Contains(item.Key))
                            {
                                warningsBuilder.AppendFormat("{0} is currently being disassembled but is {1} units below quota", itemName, missingCount).AppendLine();
                            }
                            else if (missingCount < 0 && workData.ActiveAssembling.Contains(item.Key))
                            {
                                warningsBuilder.AppendFormat("{0} is currently being assembled but is {1} units above quota", itemName, missingCount).AppendLine();
                            }
                            else
                            {
                                warningsBuilder.AppendFormat("No available assemblers to handle {0} {1}", missingCount > 0 ? "missing" : "excess", itemName).AppendLine();
                            }
                        }
                    }

                    if (warningsBuilder != null)
                    {
                        Util.TrimTrailingWhitespace(warningsBuilder);
                    }

                    foreach (var availability in workData.MissingItems)
                    {
                        if (availability.Value == MyFixedPoint.Zero)
                        {
                            continue;
                        }

                        string friendlyName = CargoSorterSessionComponent.Instance.GetFriendlyTypeName(availability.Key);
                        var group = groups.GetValueOrNew(friendlyName);
                        group[availability.Key] = group.GetValueOrDefault(availability.Key) + availability.Value;
                        groups[friendlyName] = group;
                    }

                    var displayStringBuilder = new StringBuilder();

                    if (workData.ItemAvailableAssemblers.Count == 0 || workData.MissingItems.Count == 0)
                    {
                        displayStringBuilder.Append("No quota changes needed.");
                    }

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
                        displayStringBuilder.AppendLine("Items:");
                        foreach (var group in groups.OrderBy(g => g.Key))
                        {
                            displayStringBuilder.AppendFormat("{0}:\n", group.Key);
                            foreach (var subTypeValue in group.Value.OrderBy(g => (float)g.Value))
                            {
                                if (subTypeValue.Value > 0)
                                {
                                    displayStringBuilder.AppendFormat("{0}: {1} missing\n", CargoSorterSessionComponent.Instance.GetFriendlyDefinitionDisplayName(subTypeValue.Key), MyFixedPoint.Ceiling(subTypeValue.Value));
                                }
                                else
                                {
                                    displayStringBuilder.AppendFormat("{0}: {1} excess\n", CargoSorterSessionComponent.Instance.GetFriendlyDefinitionDisplayName(subTypeValue.Key), MyFixedPoint.Ceiling(-subTypeValue.Value));
                                }
                            }

                            displayStringBuilder.AppendLine();
                        }
                    }

                    Util.TrimTrailingWhitespace(displayStringBuilder);

                    var stringToShow = "Quota Check Complete";

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

                    MyAPIGateway.Utilities.ShowMissionScreen("Quota Manager", string.Empty, stringToShow, displayStringBuilder.ToString(), (clickResult) =>
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
    }
}
