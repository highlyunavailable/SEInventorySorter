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

        private bool TryGetBlueprintDefinitionsByResultId(MyDefinitionId definitionId, out List<MyBlueprintDefinitionBase> definitions) { return _resultToBlueprints.TryGetValue(definitionId, out definitions); }

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
            JobTask = MyAPIGateway.Parallel.Start(SetProductionQuotasAction, SetProductionQuotasCallback, workData);
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

        private void SetProductionQuotasAction(WorkData data)
        {
            try
            {
                var workData = (QuotaManagerWorkData)data;
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
                            if (Instance.TryGetBlueprintDefinitionsByResultId(item.Key, out blueprintDefinitions))
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
                            if (Instance.TryGetBlueprintDefinitionsByResultId(item.Key, out blueprintDefinitions))
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

        private void GatherQuotaAndAssemblers(IEnumerable<IMyTerminalBlock> blocks, QuotaManagerWorkData workData)
        {
            //MyLog.Default.WriteLineAndConsole($"CargoSort: Quota: getting all assemblers for assembler group {workData.QuotaInfo.GroupName}");
            foreach (var block in blocks)
            {
                if (!Util.IsValid(block) || block.InventoryCount == 0 || !block.HasLocalPlayerAccess() || IsIgnored(block))
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
                    gatherInventoryContents = block.CustomName.InsensitiveContains(Instance.Config.QuotaContainerKeyword);
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

        private void SetProductionQuotasCallback(WorkData data)
        {
            JobTask = new Task();
            var workData = (QuotaManagerWorkData)data;

            ExecuteQueueChanges(workData);
            DisplayQuotaResults(workData);
        }

        private void ExecuteQueueChanges(QuotaManagerWorkData workData)
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
                if (!Instance.TryGetBlueprintDefinitionsByResultId(quotaItem.ItemId, out blueprints) || blueprints.Count == 0)
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

        private void DisplayQuotaResults(QuotaManagerWorkData workData)
        {
            switch (workData.ResultsType)
            {
                case ResultsDisplayType.Chat:
                    if (Config.ShowProgressNotifications && workData.QuotaInfo.RequestStatus == RequestValidationStatus.Valid)
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
                                        if (!TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
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
                                        if (!TryGetNormalizedItemDefinition(iniKey.Name, out definitionId))
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

                            var itemName = GetFriendlyDefinitionDisplayName(item.Key);
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

                        string friendlyName = GetFriendlyTypeName(availability.Key);
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
                                    displayStringBuilder.AppendFormat("{0}: {1} missing\n", GetFriendlyDefinitionDisplayName(subTypeValue.Key), MyFixedPoint.Ceiling(subTypeValue.Value));
                                }
                                else
                                {
                                    displayStringBuilder.AppendFormat("{0}: {1} excess\n", GetFriendlyDefinitionDisplayName(subTypeValue.Key), MyFixedPoint.Ceiling(-subTypeValue.Value));
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
                        if (Config.CopyResultsToClipboard && groups.Count > 0 && clickResult == ResultEnum.OK)
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
                    }, Config.CopyResultsToClipboard && groups.Count > 0 ? "Copy to Clipboard" : null);

                    break;
            }
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