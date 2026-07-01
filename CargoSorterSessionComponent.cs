using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CoreSystems.Api;
using InventorySorter.TerminalControls;
using InventorySorter.VirtualInventory;
using ParallelTasks;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Interfaces;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace InventorySorter
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class CargoSorterSessionComponent : MySessionComponentBase
    {
        public static CargoSorterSessionComponent Instance { get; private set; }
        public CargoSorterConfiguration Config { get; private set; }

        internal HashSet<MyDefinitionId> AllOres { get; } = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> AllIngots { get; } = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> AllComponents { get; } = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> AllAmmo { get; } = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> AllTools { get; } = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> AllBottles { get; } = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> AllConsumables { get; } = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> AllIngredients { get; } = new HashSet<MyDefinitionId>();

        private readonly Dictionary<MyDefinitionId, List<MyBlueprintDefinitionBase>> _resultToBlueprints = new Dictionary<MyDefinitionId, List<MyBlueprintDefinitionBase>>();

        private readonly MyObjectBuilderType _seedTypeId = MyObjectBuilderType.Parse("MyObjectBuilder_SeedItem");

        private readonly Dictionary<MyDefinitionId, bool> _blockConveyorSupport = new Dictionary<MyDefinitionId, bool>
        {
            { new MyDefinitionId(typeof(MyObjectBuilder_InteriorTurret), "LargeInteriorTurret"), false }
        };

        private static readonly Regex QuotedParsePattern = new Regex(@"[^\s""']+|""([^""]*)""|'([^']*)'", RegexOptions.Compiled | RegexOptions.Multiline);

        internal WcApi WcApi { get; } = new WcApi();
        private readonly HashSet<MyDefinitionId> _weapons = new HashSet<MyDefinitionId>();
        internal HashSet<MyDefinitionId> IgnoredAmmoWeapons { get; } = new HashSet<MyDefinitionId>();
        internal Dictionary<string, MyDefinitionId> WcAmmoMagazines { get; } = new Dictionary<string, MyDefinitionId>();
        internal readonly MyDefinitionId IgnoredEnergyAmmoDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), "Energy");

        private readonly Dictionary<string, MyDefinitionId> _stringPhysicalItemMap = new Dictionary<string, MyDefinitionId>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<MyObjectBuilderType, string> _friendlyTypeNames = new Dictionary<MyObjectBuilderType, string>();

        internal Task JobTask;

        private string _autoSortProfile;
        internal IMyShipController AutoSortingController;
        internal int AutoSortTicksRemaining;
        public Dictionary<MyDefinitionId, MyFixedPoint> LastMissingItems { get; private set; } = new Dictionary<MyDefinitionId, MyFixedPoint>();
        internal long LastSortTick { get; set; }

        public override void LoadData()
        {
            if (Util.IsDedicatedServer)
            {
                return;
            }

            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;

            LoadSettings();

            Instance = this;

            // Have to do this because Keen doesn't provide multiple BPs per item in
            // MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId
            // so stuff like stone to ingot is not considered normally.
            foreach (var definition in MyDefinitionManager.Static.GetBlueprintDefinitions())
            {
                foreach (var result in definition.Results)
                {
                    List<MyBlueprintDefinitionBase> bpList;
                    if (_resultToBlueprints.TryGetValue(result.Id, out bpList))
                    {
                        if (!bpList.Contains(definition))
                        {
                            bpList.Add(definition);
                        }
                    }
                    else
                    {
                        bpList = new List<MyBlueprintDefinitionBase>
                        {
                            definition
                        };
                        _resultToBlueprints.Add(result.Id, bpList);
                    }
                }
            }

            // Do we need to sort these by something based on value? Unlikely since vanilla tends to
            // have 1 recipe per block per result, but sort by fastest for now.
            foreach (var resultDef in _resultToBlueprints)
            {
                resultDef.Value.SortNoAlloc((x, y) => x.BaseProductionTimeInSeconds.CompareTo(y.BaseProductionTimeInSeconds));
            }

            foreach (var definition in MyDefinitionManager.Static.GetPhysicalItemDefinitions())
            {
                if (!definition.Enabled)
                {
                    continue;
                }

                if (definition.IsOre)
                {
                    AllOres.Add(definition.Id);
                    MakeNormalizedId(definition.Id, "Ore");
                }

                if (definition.IsIngot)
                {
                    AllIngots.Add(definition.Id);
                    MakeNormalizedId(definition.Id, "Ingot");
                }

                if (definition is MyConsumableItemDefinition)
                {
                    bool isInRecipe = false;
                    foreach (var blueprint in MyDefinitionManager.Static.GetBlueprintDefinitions())
                    {
                        foreach (var prereq in blueprint.Prerequisites)
                        {
                            if (prereq.Id == definition.Id)
                            {
                                isInRecipe = true;
                                goto checkForRecipe;
                            }
                        }
                    }

                    checkForRecipe:
                    if (!isInRecipe)
                    {
                        AllConsumables.Add(definition.Id);
                        MakeNormalizedId(definition.Id, "Item");
                    }
                    else
                    {
                        AllIngredients.Add(definition.Id);
                        MakeNormalizedId(definition.Id, "Item");
                    }
                }

                if (definition.Id.TypeId == _seedTypeId)
                {
                    AllIngredients.Add(definition.Id);
                    MakeNormalizedId(definition.Id, "Seed");
                }

                if (definition is MyDatapadDefinition || definition is MyPackageDefinition)
                {
                    AllTools.Add(definition.Id);
                    MakeNormalizedId(definition.Id, "Item");
                }

                if (definition.Id.TypeId == typeof(MyObjectBuilder_PhysicalObject))
                {
                    if (!(AllConsumables.Contains(definition.Id) || AllIngredients.Contains(definition.Id) || AllTools.Contains(definition.Id)))
                    {
                        bool isInRecipe = false;
                        foreach (var consumable in AllConsumables)
                        {
                            List<MyBlueprintDefinitionBase> blueprints;
                            if (TryGetBlueprintDefinitionsByResultId(consumable, out blueprints))
                            {
                                foreach (var blueprint in blueprints)
                                {
                                    foreach (var prereq in blueprint.Prerequisites)
                                    {
                                        if (prereq.Id == definition.Id)
                                        {
                                            isInRecipe = true;
                                            goto checkForRecipe;
                                        }
                                    }
                                }
                            }
                        }

                        checkForRecipe:
                        if (!isInRecipe)
                        {
                            AllTools.Add(definition.Id);
                            MakeNormalizedId(definition.Id, "Item");
                        }
                        else
                        {
                            AllIngredients.Add(definition.Id);
                            MakeNormalizedId(definition.Id, "Item");
                        }
                    }
                }

                if (definition is MyOxygenContainerDefinition)
                {
                    AllBottles.Add(definition.Id);
                    MakeNormalizedId(definition.Id, "Bottle");
                }

                if (definition is MyComponentDefinition)
                {
                    AllComponents.Add(definition.Id);
                    MakeNormalizedId(definition.Id, "Component");
                }

                if (definition is MyAmmoMagazineDefinition)
                {
                    AllAmmo.Add(definition.Id);
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
                    AllTools.Add(handPhysicalItem.Id);
                }
            }

            foreach (var def in MyDefinitionManager.Static.GetDefinitionsOfType<MyWeaponBlockDefinition>())
            {
                _weapons.Add(def.Id);
            }

            foreach (var def in MyDefinitionManager.Static.GetDefinitionsOfType<MyWarheadDefinition>())
            {
                _weapons.Add(def.Id);
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
            if (!WcApi.IsReady)
            {
                WcApi.Load(OnWcReady, true);
            }
        }

        private void OnWcReady()
        {
            var allCoreWeapons = new HashSet<MyDefinitionId>();
            WcApi.GetAllCoreWeapons(allCoreWeapons);
            _weapons.UnionWith(allCoreWeapons);
            var allWeaponMagazines = new Dictionary<MyDefinitionId, List<MyTuple<int, MyTuple<MyDefinitionId, string, string, bool>>>>();
            WcApi.GetAllWeaponMagazines(allWeaponMagazines);
            foreach (var weaponMagazines in allWeaponMagazines)
            {
                var shouldIgnore = true;
                foreach (var magazine in weaponMagazines.Value)
                {
                    if (magazine.Item2.Item1 == IgnoredEnergyAmmoDefinitionId)
                    {
                        continue;
                    }

                    WcAmmoMagazines[magazine.Item2.Item3] = magazine.Item2.Item1;
                    shouldIgnore = false;
                    break;
                }

                if (shouldIgnore)
                {
                    IgnoredAmmoWeapons.Add(weaponMagazines.Key);
                }
            }
        }

        private void MakeNormalizedId(MyDefinitionId definitionId, string friendlyType)
        {
            var friendlyTypeLower = friendlyType.ToLowerInvariant();
            var normalizedStringId = definitionId.ToString().Replace(MyObjectBuilderType.LEGACY_TYPE_PREFIX, string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            _stringPhysicalItemMap[normalizedStringId] = definitionId;
            if (normalizedStringId != friendlyTypeLower)
            {
                var normalizedFriendlyId = $"{friendlyType}/{definitionId.SubtypeName}".ToLowerInvariant();
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Adding friendly type {normalizedFriendlyId} -> {definition.Id}");
                _stringPhysicalItemMap[normalizedFriendlyId] = definitionId;
                _friendlyTypeNames[definitionId.TypeId] = friendlyType;
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
            if (_stringPhysicalItemMap.TryGetValue(shortStringName, out definitionId))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Normalized type lookup {shortStringName} -> {definitionId}");
                return true;
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Normalized type lookup {shortStringName} failed");
            return false;
        }

        internal string GetFriendlyTypeName(MyDefinitionId definitionId)
        {
            string friendlyName;
            if (_friendlyTypeNames.TryGetValue(definitionId.TypeId, out friendlyName))
            {
                //MyLog.Default.WriteLineAndConsole($"CargoSort: Friendly type lookup {definitionId.TypeId} -> {friendlyName}");
                return friendlyName;
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Friendly type lookup {definitionId.TypeId} failed");
            return definitionId.TypeId.ToString().Replace(MyObjectBuilderType.LEGACY_TYPE_PREFIX, "");
        }

        public string GetFriendlyDefinitionName(MyDefinitionId definitionId) { return $"{GetFriendlyTypeName(definitionId)}/{definitionId.SubtypeName}"; }

        public string GetFriendlyDefinitionDisplayName(MyDefinitionId definitionId)
        {
            var def = MyDefinitionManager.Static.GetDefinition(definitionId);
            if (def == null || Instance.Config.DisableShowItemName)
            {
                return GetFriendlyDefinitionName(definitionId);
            }

            return $"{def.DisplayNameText} ({GetFriendlyDefinitionName(definitionId)})";
        }

        internal bool TryGetBlueprintDefinitionsByResultId(MyDefinitionId definitionId, out List<MyBlueprintDefinitionBase> definitions) { return _resultToBlueprints.TryGetValue(definitionId, out definitions); }

        public static bool TryGetPhysicalItemProperties(MyDefinitionId definitionId, out float volume, out float mass, out bool hasIntegralAmounts)
        {
            MyPhysicalItemDefinition physItem;
            var validItem = MyDefinitionManager.Static.TryGetPhysicalItemDefinition(definitionId, out physItem);
            if (validItem)
            {
                volume = physItem.Volume;
                mass = physItem.Mass;
                hasIntegralAmounts = physItem.HasIntegralAmounts;
                return true;
            }

            volume = 0;
            mass = 0;
            hasIntegralAmounts = false;
            return false;
        }

        public bool CanAssemblerBuild(IMyAssembler assembler, KeyValuePair<MyDefinitionId, MyFixedPoint> item)
        {
            if (!Util.IsValid(assembler))
            {
                return false;
            }

            List<MyBlueprintDefinitionBase> blueprintDefinitions;
            if (TryGetBlueprintDefinitionsByResultId(item.Key, out blueprintDefinitions))
            {
                foreach (var blueprintDefinition in blueprintDefinitions)
                {
                    if (assembler.CanUseBlueprint(blueprintDefinition))
                    {
                        return true;
                    }
                }
            }

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

                var profile = ExtractProfileFromMessage(messageText);
                BeginSortJob(shipController.CubeGrid, profile, ResultsDisplayType.Chat);
            }
            else if (messageText.StartsWith("/csort", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                var shipController = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyShipController;
                if (shipController == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "You must be seated on a grid to sort!");
                    return;
                }

                var profile = ExtractProfileFromMessage(messageText);
                BeginConstructSortJob(shipController.CubeGrid, profile, ResultsDisplayType.Chat);
            }
            else if (messageText.StartsWith("/asort", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                var shipController = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyShipController;
                if (shipController == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "You must be seated on a grid to auto sort!");
                    return;
                }

                if (!shipController.IsMainCockpit || !shipController.CanControlShip)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "You must be seated in the main cockpit and be controlling the ship to auto sort!");
                    return;
                }

                if (AutoSortingController == shipController)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "Already auto sorting");
                    return;
                }

                AutoSortingController = shipController;
                AutoSortTicksRemaining = 0;
                SetUpdateOrder(MyUpdateOrder.BeforeSimulation);
                MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntityChanged += OnControlledEntityChanged;
                _autoSortProfile = ExtractProfileFromMessage(messageText);
                if (string.IsNullOrWhiteSpace(_autoSortProfile))
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "Auto sorting started");
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Auto sorting ({_autoSortProfile}) started");
                }

                BeginConstructSortJob(shipController.CubeGrid, _autoSortProfile, ResultsDisplayType.None);
            }
            else if (messageText.StartsWith("/stopsort", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                DisableAutoSort();
            }
            else if (messageText.StartsWith("/getallsortableitems", StringComparison.OrdinalIgnoreCase))
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
            else if (messageText.StartsWith("/copycd", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                var shipController = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyShipController;
                if (shipController == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "You must be seated on a grid to copy custom data, as the pattern references the current grid");
                    return;
                }

                var matches = QuotedParsePattern.Matches(messageText);
                if (matches.Count != 3)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "Incorrect copy custom data arguments: The command must be in the form of '/copycd \"Source Pattern\" \"Target Pattern\"'");
                    return;
                }

                Util.CopyCustomData(shipController.CubeGrid, matches[1].Value, matches[2].Value);
            }
            else if (messageText.StartsWith("/splitcd", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;
                var shipController = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyShipController;
                if (shipController == null)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "You must be seated on a grid to split custom data, as the pattern references the current grid");
                    return;
                }

                var matches = QuotedParsePattern.Matches(messageText);
                if (matches.Count < 3 || matches.Count > 4)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "Incorrect split custom data arguments: The command must be in the form of '/copycd \"Source Pattern\" \"Target Pattern\" \"Profile\"' (profile is optional).");
                    return;
                }

                Util.SplitCustomData(shipController.CubeGrid, matches[1].Value, matches[2].Value, matches.Count == 4 ? matches[3].Value : null);
            }
            else if (messageText.StartsWith("/configuresorter", StringComparison.OrdinalIgnoreCase))
            {
                sendToOthers = false;

                if (JobTask.valid && !JobTask.IsComplete)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "Cannot change settings while a job is in progress!");
                    return;
                }

                var matches = QuotedParsePattern.Matches(messageText);

                if (matches.Count < 2 || matches.Count >= 4)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", "Incorrect chat configuration command. To see valid arguments, run '/configuresorter help'.");
                    return;
                }

                if (matches[1].Value.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    CargoSorterConfiguration.ShowHelp();
                    return;
                }

                if (matches.Count == 3)
                {
                    CargoSorterConfiguration.ChangeSettingsFromChat(matches[1].Value, matches[2].Value);
                    return;
                }

                MyAPIGateway.Utilities.ShowMessage("Sorter", "Incorrect chat configuration command. To see valid arguments, run '/configuresorter help'.");
            }
        }

        private static string ExtractProfileFromMessage(string messageText)
        {
            string profile = null;
            var spaceIndex = messageText.IndexOf(' ');

            if (spaceIndex != -1 && messageText.Length > spaceIndex)
            {
                profile = messageText.Substring(spaceIndex + 1).Trim().ToLowerInvariant();
            }

            return profile;
        }

        private void OnControlledEntityChanged(IMyControllableEntity lastEntity, IMyControllableEntity currentEntity)
        {
            var currentBlock = currentEntity as IMyTerminalBlock;
            if (AutoSortingController != null && currentBlock != null && AutoSortingController.CubeGrid.IsSameConstructAs(currentBlock.CubeGrid))
            {
                return;
            }

            DisableAutoSort();
        }

        private void DisableAutoSort()
        {
            MyAPIGateway.Utilities.ShowMessage("Sorter", "Auto sorting stopped");
            SetUpdateOrder(MyUpdateOrder.NoUpdate);
            AutoSortTicksRemaining = 0;
            _autoSortProfile = null;
            AutoSortingController = null;
            MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntityChanged -= OnControlledEntityChanged;
        }

        public override void UpdateBeforeSimulation()
        {
            AutoSortTicksRemaining--;
            if (AutoSortTicksRemaining > 0 || !JobTask.IsComplete)
            {
                return;
            }

            var currentBlock = MyAPIGateway.Session.LocalHumanPlayer.Controller.ControlledEntity as IMyTerminalBlock;
            if (AutoSortingController == null || currentBlock == null || !AutoSortingController.CubeGrid.IsSameConstructAs(currentBlock.CubeGrid))
            {
                DisableAutoSort();
                return;
            }

            BeginConstructSortJob(AutoSortingController.CubeGrid, _autoSortProfile, ResultsDisplayType.None);
        }

        public void BeginSortJob(IMyCubeGrid rootGrid, string profile, ResultsDisplayType resultsDisplayType)
        {
            if (JobTask.valid && !JobTask.IsComplete)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", "A job is already in progress!");
                return;
            }

            var workData = new CargoSorterWorkData(rootGrid, profile, false, resultsDisplayType);
            JobTask = MyAPIGateway.Parallel.Start(SortingEngine.Run, SortingEngine.OnComplete, workData);
        }

        public void BeginConstructSortJob(IMyCubeGrid rootGrid, string profile, ResultsDisplayType resultsDisplayType)
        {
            if (JobTask.valid && !JobTask.IsComplete)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", "A job is already in progress!");
                return;
            }

            var workData = new CargoSorterWorkData(rootGrid, profile, true, resultsDisplayType);
            JobTask = MyAPIGateway.Parallel.Start(SortingEngine.Run, SortingEngine.OnComplete, workData);
        }

        public void BeginQuotaJob(IMyAssembler assembler, ResultsDisplayType resultsDisplayType)
        {
            if (JobTask.valid && !JobTask.IsComplete)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", "A job is already in progress!");
                return;
            }

            var workData = new QuotaManagerWorkData(assembler, new ProductionQuotaInfo(assembler), resultsDisplayType);
            JobTask = MyAPIGateway.Parallel.Start(QuotaEngine.Run, QuotaEngine.OnComplete, workData);
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

            return queuePrerequisites.Count > 0 ? InventoryInfo.BuildCustomData(queuePrerequisites, true) : string.Empty;
        }

        public string GenerateResultCustomDataFromQueue(IMyAssembler assembler)
        {
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

            return queueResults.Count > 0 ? InventoryInfo.BuildCustomData(queueResults, true) : string.Empty;
        }

        public void GenerateQueueFromItemList(IMyAssembler assembler, Dictionary<MyDefinitionId, MyFixedPoint> items)
        {
            foreach (var item in items)
            {
                List<MyBlueprintDefinitionBase> blueprintDefinitions;
                if (!Instance.TryGetBlueprintDefinitionsByResultId(item.Key, out blueprintDefinitions))
                {
                    continue;
                }

                // Find usable blueprint
                foreach (var blueprint in blueprintDefinitions)
                {
                    if (!assembler.CanUseBlueprint(blueprint))
                    {
                        continue;
                    }

                    if (item.Value <= MyFixedPoint.Zero)
                    {
                        continue;
                    }

                    assembler.AddQueueItem(blueprint, item.Value);
                    break;
                }
            }
        }

        public Dictionary<MyDefinitionId, MyTuple<MyFixedPoint, bool>> GenerateQueueFromCustomData(IMyAssembler assembler)
        {
            var inputInventory = assembler?.OutputInventory as MyInventory;
            if (inputInventory == null)
            {
                return new Dictionary<MyDefinitionId, MyTuple<MyFixedPoint, bool>>();
            }

            var inventoryInfo = new InventoryInfo(inputInventory, "Inventory");

            if (inventoryInfo.Requests == null)
            {
                return new Dictionary<MyDefinitionId, MyTuple<MyFixedPoint, bool>>();
            }

            var queued = new Dictionary<MyDefinitionId, MyTuple<MyFixedPoint, bool>>(inventoryInfo.Requests.Count);
            foreach (var request in inventoryInfo.Requests)
            {
                queued[request.DefinitionId] = new MyTuple<MyFixedPoint, bool>(request.Amount, false);
                List<MyBlueprintDefinitionBase> blueprintDefinitions;
                if (!Instance.TryGetBlueprintDefinitionsByResultId(request.DefinitionId, out blueprintDefinitions))
                {
                    continue;
                }

                // Find usable blueprint
                foreach (var blueprint in blueprintDefinitions)
                {
                    if (!assembler.CanUseBlueprint(blueprint))
                    {
                        continue;
                    }

                    if (request.Amount == MyFixedPoint.Zero)
                    {
                        continue;
                    }

                    assembler.AddQueueItem(blueprint, request.Amount);
                    queued[request.DefinitionId] = new MyTuple<MyFixedPoint, bool>(request.Amount, true);
                    break;
                }
            }

            return queued;
        }

        public string GenerateCustomDataFromProjector(IMyProjector projector)
        {
            var projectorProxy = new ProjectorProxy(projector);
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

            return components.Count > 0 ? InventoryInfo.BuildCustomData(components, true) : string.Empty;
        }

        private void CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (block is IMyShipController)
            {
                ShipControllerTerminalControls.EnsureControlSetup();
                actions.AddRange(ShipControllerTerminalControls.Actions);
            }
            else if (block is IMyAssembler)
            {
                AssemblerTerminalControls.EnsureControlSetup();
                actions.AddRange(AssemblerTerminalControls.Actions);
            }
            else if (block is IMyProjector)
            {
                ProjectorTerminalControls.EnsureControlSetup();
                actions.AddRange(ProjectorTerminalControls.Actions);
            }
        }

        private void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block is IMyShipController)
            {
                ShipControllerTerminalControls.EnsureControlSetup();
                controls.AddRange(ShipControllerTerminalControls.Controls);
            }
            else if (block is IMyAssembler)
            {
                AssemblerTerminalControls.EnsureControlSetup();
                controls.AddRange(AssemblerTerminalControls.Controls);
            }
            else if (block is IMyProjector)
            {
                ProjectorTerminalControls.EnsureControlSetup();
                controls.AddRange(ProjectorTerminalControls.Controls);
            }

            if (CargoTerminalControls.AllowSwapInventory(block))
            {
                CargoTerminalControls.EnsureControlSetup();
                controls.AddRange(CargoTerminalControls.Controls);
            }
        }

        private string BuildAllSortableItemNamesString()
        {
            var sbMap = new StringBuilder();
            var sbInv = new StringBuilder();
            sbMap.AppendLine("Sortable Items ([Sortable ID] is [Display Name]) - scroll down for inventory format");
            foreach (var item in MakeSortedIdDefs(AllOres))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            foreach (var item in MakeSortedIdDefs(AllIngots))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            foreach (var item in MakeSortedIdDefs(AllComponents))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            foreach (var item in MakeSortedIdDefs(AllAmmo))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            foreach (var item in MakeSortedIdDefs(AllTools))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            foreach (var item in MakeSortedIdDefs(AllBottles))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            foreach (var item in MakeSortedIdDefs(AllIngredients))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            foreach (var item in MakeSortedIdDefs(AllConsumables))
            {
                sbMap.AppendFormat("{0} is {1}\n", item.Key, item.Value);
                sbInv.AppendFormat("{0}=All\n", item.Key);
            }

            sbMap.AppendLine().AppendLine("Inventory/Quota Custom Data format:");
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

            if (WcApi.IsReady)
            {
                WcApi.Unload();
            }

            MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter -= CustomActionGetter;
            MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
            Instance = null;
        }

        // Copied in part from BuildVision. Thanks Digi!
        internal bool IsIgnored(IMyTerminalBlock block)
        {
            foreach (var item in Config.LockedContainerKeywords)
            {
                if (block.CustomName.InsensitiveContains(item))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasConveyorSupport(IMyCubeBlock block)
        {
            if (!Util.IsValid(block))
            {
                return false;
            }

            bool supportsConveyors;
            if (_blockConveyorSupport.TryGetValue(block.BlockDefinition, out supportsConveyors))
            {
                return supportsConveyors;
            }

            var dummies = new Dictionary<string, IMyModelDummy>();
            block.Model.GetDummies(dummies);

            foreach (var dummy in dummies)
            {
                if (dummy.Value.Name.StartsWith("detector_conveyor", StringComparison.OrdinalIgnoreCase))
                {
                    supportsConveyors = true;
                    break;
                }
            }

            _blockConveyorSupport.Add(block.BlockDefinition, supportsConveyors);
            return supportsConveyors;
        }

        public bool IsWeapon(IMyCubeBlock block) { return block is IMyUserControllableGun || _weapons.Contains(block.BlockDefinition); }

        public void LoadSettings()
        {
            try
            {
                Config = CargoSorterConfiguration.LoadSettings();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"CargoSort: Exception loading settings: {e.Message}");
            }
        }
    }
}