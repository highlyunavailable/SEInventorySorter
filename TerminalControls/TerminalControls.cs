using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace InventorySorter.TerminalControls
{
    public static class TerminalControls
    {
        public static volatile bool CockpitControls;
        public static volatile bool RemoteControlControls;
        public static volatile bool ProjectorControls;
        public static volatile bool AssemblerControls;
        public static volatile bool CargoContainerControls;

        private static readonly HashSet<Type> TypeActivated = new HashSet<Type>();

        private static bool ControlsActivated<T>()
        {
            return !TypeActivated.Add(typeof(T));
        }

        public static void CreateTerminalBlockControls<T>() where T : IMyTerminalBlock
        {
            if (ControlsActivated<T>())
            {
                return;
            }

            MyLog.Default.WriteLineAndConsole($"Creating terminal block controls for {typeof(T).Name}");
            AddSwapInventoriesControl<T>();
        }

        public static void CreateShipControllerControls<T>() where T : IMyShipController
        {
            if (ControlsActivated<T>())
            {
                return;
            }

            MyLog.Default.WriteLineAndConsole($"Creating ship controller controls for {typeof(T).Name}");
            AddSortInventoriesAction<T>();
            AddSortConstructAction<T>();
            AddSortInventoriesControl<T>();
            AddSortConstructControl<T>();
            AddSwapInventoriesControl<T>();
        }

        public static void CreateProjectorControls<T>() where T : IMyProjector
        {
            if (ControlsActivated<T>())
            {
                return;
            }


            MyLog.Default.WriteLineAndConsole($"Creating projector controls for {typeof(T).Name}");
            AddProjectorCreateDataControl<T>();
        }

        public static void CreateAssemblerControls<T>() where T : IMyAssembler
        {
            if (ControlsActivated<T>())
            {
                return;
            }

            MyLog.Default.WriteLineAndConsole($"Creating assembler controls for {typeof(T).Name}");
            AddAssemblerQuotaProductionAction<T>();
            AddAssemblerClearQueueAction<T>();

            AddAssemblerClearQueueControl<T>();
            AddAssemblerQuotaProductionControl<T>();
            AddAssemblerGenerateQueueControl<T>();
            AddAssemblerMakePrerequisiteDataControl<T>();
            AddAssemblerMakeResultDataControl<T>();
        }

        private static void AddSwapInventoriesControl<T>() where T : IMyTerminalBlock
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_SwapWithCharacterInventory");
            control.Title = MyStringId.GetOrCompute("Swap Inventories");
            control.Tooltip = MyStringId.GetOrCompute("Swap your character's items with the items in this block");
            control.SupportsMultipleBlocks = false;
            control.Visible = SwapInventoriesControlVisible;
            control.Action = SwapInventoriesControlAction;

            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static void SwapInventoriesControlAction(IMyTerminalBlock block)
        {
            var character = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (!Util.IsValid(character) || !Util.IsValid(block) || block.InventoryCount != 1)
            {
                return;
            }

            var containerInventory = block.GetInventory(0) as MyInventory;
            var characterInventory = character.GetInventory(0) as MyInventory;

            if (containerInventory == null || characterInventory == null || containerInventory.CurrentVolume >= characterInventory.MaxVolume || characterInventory.CurrentVolume >= containerInventory.MaxVolume)
            {
                return;
            }

            var containerItems = new List<MyPhysicalInventoryItem>(containerInventory.GetItems());
            var characterItems = new List<MyPhysicalInventoryItem>(characterInventory.GetItems());

            for (var i = 0; i < Math.Max(containerItems.Count, characterItems.Count); i++)
            {
                if (i <= containerItems.Count - 1 && i <= characterItems.Count - 1)
                {
                    var charItem = characterItems[i];
                    var contItem = containerItems[i];
                    if (characterInventory.MaxVolume - characterInventory.CurrentVolume > containerInventory.MaxVolume - containerInventory.CurrentVolume)
                    {
                        MyInventory.TransferByUser(containerInventory, characterInventory, contItem.ItemId, i, contItem.Amount);
                        MyInventory.TransferByUser(characterInventory, containerInventory, charItem.ItemId, i, charItem.Amount);
                    }
                    else
                    {
                        MyInventory.TransferByUser(characterInventory, containerInventory, charItem.ItemId, i, charItem.Amount);
                        MyInventory.TransferByUser(containerInventory, characterInventory, contItem.ItemId, i, contItem.Amount);
                    }

                    continue;
                }

                if (i >= containerItems.Count)
                {
                    var excessItem = characterItems[i];
                    MyInventory.TransferByUser(characterInventory, containerInventory, excessItem.ItemId, -1);
                }

                if (i >= characterItems.Count)
                {
                    var excessItem = containerItems[i];
                    MyInventory.TransferByUser(containerInventory, characterInventory, excessItem.ItemId, -1);
                }
            }
        }

        private static bool SwapInventoriesControlVisible(IMyTerminalBlock block)
        {
            if (!Util.IsValid(block) || block.InventoryCount != 1)
            {
                return false;
            }

            var character = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (!Util.IsValid(character))
            {
                return false;
            }

            var containerInventory = block.GetInventory(0) as MyInventory;
            var characterInventory = character.GetInventory(0) as MyInventory;

            if (containerInventory == null || characterInventory == null)
            {
                return false;
            }

            return containerInventory.CurrentVolume <= characterInventory.MaxVolume && characterInventory.CurrentVolume <= containerInventory.MaxVolume;
        }


        private static void AddSortInventoriesAction<T>() where T : IMyShipController
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("CargoSort_Sort");
            action.Enabled = SortInventoryControlVisible;
            action.Name = new StringBuilder("Sort Inventory");
            action.Icon = @"Textures\GUI\Icons\Actions\Reverse.dds";
            action.ValidForGroups = false;
            action.InvalidToolbarTypes = new List<MyToolbarType>()
            {
                MyToolbarType.Character,
                MyToolbarType.ButtonPanel,
                MyToolbarType.Seat,
            };
            action.Action = StartSortToolbarAction;

            MyAPIGateway.TerminalControls.AddAction<T>(action);
        }

        private static void AddSortConstructAction<T>() where T : IMyShipController
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("CargoSort_SortConstruct");
            action.Enabled = SortInventoryControlVisible;
            action.Name = new StringBuilder("Sort Construct Inventory");
            action.Icon = @"Textures\GUI\Icons\Actions\Reverse.dds";
            action.ValidForGroups = false;
            action.InvalidToolbarTypes = new List<MyToolbarType>()
            {
                MyToolbarType.Character,
                MyToolbarType.ButtonPanel,
                MyToolbarType.Seat,
            };
            action.Action = StartSortConstructToolbarAction;

            MyAPIGateway.TerminalControls.AddAction<T>(action);
        }


        private static void AddSortInventoriesControl<T>() where T : IMyShipController
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_SortButton");
            control.Title = MyStringId.GetOrCompute("Sort Inventory");
            control.Tooltip = MyStringId.GetOrCompute("Sorts the inventory of the current grid and all attached grids");
            control.SupportsMultipleBlocks = false;
            control.Visible = SortInventoryControlVisible;
            control.Action = StartSortButtonAction;

            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static void AddSortConstructControl<T>() where T : IMyShipController
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_SortConstructButton");
            control.Title = MyStringId.GetOrCompute("Sort Construct Inventory");
            control.Tooltip = MyStringId.GetOrCompute("Sorts the inventory of the current grid and all physically attached sub-grids");
            control.SupportsMultipleBlocks = false;
            control.Visible = SortInventoryControlVisible;
            control.Action = StartSortConstructButtonAction;

            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static bool SortInventoryControlVisible(IMyTerminalBlock block) => Util.IsValid(block);

        private static void StartSortToolbarAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginSortJob(block.CubeGrid, null, ResultsDisplayType.Chat);
            }
        }

        private static void StartSortConstructToolbarAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginConstructSortJob(block.CubeGrid, null, ResultsDisplayType.Chat);
            }
        }

        private static void StartSortButtonAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginSortJob(block.CubeGrid, null, ResultsDisplayType.Window);
            }
        }

        private static void StartSortConstructButtonAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginConstructSortJob(block.CubeGrid, null, ResultsDisplayType.Window);
            }
        }

        private static void AddProjectorCreateDataControl<T>() where T : IMyProjector
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_GenerateCustomDataFromProjectionButton");
            control.Title = MyStringId.GetOrCompute("Make Data from Projection");
            control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data from the projected grid. Paste this into an assembler to queue it!");
            control.SupportsMultipleBlocks = false;
            control.Enabled = HasProjectedGrid;
            control.Action = GenerateCustomDataFromProjectionAction;
            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static bool HasProjectedGrid(IMyTerminalBlock block) => Util.IsValid(block) && (block as IMyProjector)?.ProjectedGrid != null;

        private static void GenerateCustomDataFromProjectionAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && block is IMyProjector && CargoSorterSessionComponent.Instance != null)
            {
                var data = CargoSorterSessionComponent.Instance.GenerateCustomDataFromProjector((IMyProjector)block);
                MyAPIGateway.Utilities.ShowMissionScreen("Generated Custom Data", $"{block.DisplayNameText}", " Grid Components", data, (clickResult) =>
                {
                    if (!string.IsNullOrWhiteSpace(data) && clickResult == ResultEnum.OK)
                    {
                        MyClipboardHelper.SetClipboard(data);
                    }
                }, !string.IsNullOrWhiteSpace(data) ? "Copy to Clipboard" : null);
            }
        }

        private static void AddAssemblerQuotaProductionAction<T>() where T : IMyAssembler
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("CargoSort_Quota");
            action.Enabled = HasQuotaCustomData;
            action.Name = new StringBuilder("Quota Production");
            action.Icon = @"Textures\GUI\Icons\Actions\Start.dds";
            action.ValidForGroups = false;
            action.InvalidToolbarTypes = new List<MyToolbarType>()
            {
                MyToolbarType.Character,
                MyToolbarType.ButtonPanel,
                MyToolbarType.Seat,
            };
            action.Action = StartQuotaAction;
            MyAPIGateway.TerminalControls.AddAction<T>(action);
        }

        private static void AddAssemblerClearQueueAction<T>() where T : IMyAssembler
        {
            var action = MyAPIGateway.TerminalControls.CreateAction<T>("CargoSort_ClearQueueAction");
            action.Name = new StringBuilder("Clear Queue");
            action.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
            action.ValidForGroups = true;
            action.InvalidToolbarTypes = new List<MyToolbarType>()
            {
                MyToolbarType.Character,
                MyToolbarType.ButtonPanel,
                MyToolbarType.Seat,
            };
            action.Action = ClearAssemblerQueueItems;
            MyAPIGateway.TerminalControls.AddAction<T>(action);
        }

        private static void AddAssemblerClearQueueControl<T>() where T : IMyAssembler
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_ClearQueueButton");
            control.Title = MyStringId.GetOrCompute("Clear Queue");
            control.Tooltip = MyStringId.GetOrCompute("Clears the queues of the selected assemblers");
            control.SupportsMultipleBlocks = true;
            control.Action = ClearAssemblerQueueItems;
            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static void AddAssemblerQuotaProductionControl<T>() where T : IMyAssembler
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_BuildToQuotaButton");
            control.Title = MyStringId.GetOrCompute("Build to Quota");
            control.Tooltip = MyStringId.GetOrCompute("Queue up items to match production quotas. Must be a primary group assembler and have quota data.");
            control.SupportsMultipleBlocks = false;
            control.Enabled = HasQuotaCustomData;
            control.Action = StartQuotaAction;
            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static void AddAssemblerMakePrerequisiteDataControl<T>() where T : IMyAssembler
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_GeneratePrerequisiteCustomDataFromQueueButton");
            control.Title = MyStringId.GetOrCompute("Make Prerequisite Data");
            control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the prerequisites for this assembler's queue");
            control.SupportsMultipleBlocks = false;
            control.Enabled = HasQueueReady;
            control.Action = GeneratePrerequisiteCustomDataFromQueueAction;
            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static void AddAssemblerMakeResultDataControl<T>() where T : IMyAssembler
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_GenerateCustomDataFromQueueButton");
            control.Title = MyStringId.GetOrCompute("Make Result Data");
            control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the results of this assembler's queue");
            control.SupportsMultipleBlocks = false;
            control.Enabled = HasQueueReady;
            control.Action = GenerateResultCustomDataFromQueueAction;
            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }

        private static void AddAssemblerGenerateQueueControl<T>() where T : IMyAssembler
        {
            var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, T>("CargoSort_GenerateQueueFromCustomDataButton");
            control.Title = MyStringId.GetOrCompute("Make Queue from Data");
            control.Tooltip = MyStringId.GetOrCompute("Queues up items from the Inventory custom data of this assembler");
            control.SupportsMultipleBlocks = false;
            control.Enabled = CanMakeQueueFromCustomData;
            control.Action = QueueFromCustomDataAction;
            MyAPIGateway.TerminalControls.AddControl<T>(control);
        }


        private static bool HasQueueReady(IMyTerminalBlock block) => Util.IsValid(block) && (block as IMyAssembler)?.IsQueueEmpty == false;

        private static bool CanMakeQueueFromCustomData(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyAssembler &&
                                                                                  !block.DisplayNameText.InsensitiveContains(CargoSorterSessionComponent.Instance?.Config?.SpecialContainerKeyword) &&
                                                                                  !block.DisplayNameText.InsensitiveContains(CargoSorterSessionComponent.Instance?.Config?.LimitedContainerKeyword) &&
                                                                                  block.CustomData.Contains("[Inventory]");

        private static bool HasQuotaCustomData(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyAssembler &&
                                                                          block.CustomData.Contains("[Quota]") && !block.DisplayNameText.InsensitiveContains("[Secondary:");


        private static void GeneratePrerequisiteCustomDataFromQueueAction(IMyTerminalBlock block)
        {
            if (!Util.IsValid(block) || !(block is IMyAssembler) || CargoSorterSessionComponent.Instance == null)
            {
                return;
            }

            var data = CargoSorterSessionComponent.Instance.GeneratePrerequisiteCustomDataFromQueue((IMyAssembler)block);
            MyAPIGateway.Utilities.ShowMissionScreen("Generated Custom Data", $"{block.DisplayNameText}", " Queue Prerequisites", data, (clickResult) =>
            {
                if (!string.IsNullOrWhiteSpace(data) && clickResult == ResultEnum.OK)
                {
                    MyClipboardHelper.SetClipboard(data);
                }
            }, !string.IsNullOrWhiteSpace(data) ? "Copy to Clipboard" : null);
        }

        private static void GenerateResultCustomDataFromQueueAction(IMyTerminalBlock block)
        {
            if (!Util.IsValid(block) || !(block is IMyAssembler) || CargoSorterSessionComponent.Instance == null)
            {
                return;
            }

            var data = CargoSorterSessionComponent.Instance.GenerateResultCustomDataFromQueue((IMyAssembler)block);
            MyAPIGateway.Utilities.ShowMissionScreen("Generated Custom Data", $"{block.DisplayNameText}", " Queue Results", data, (clickResult) =>
            {
                if (!string.IsNullOrWhiteSpace(data) && clickResult == ResultEnum.OK)
                {
                    MyClipboardHelper.SetClipboard(data);
                }
            }, !string.IsNullOrWhiteSpace(data) ? "Copy to Clipboard" : null);
        }

        private static void QueueFromCustomDataAction(IMyTerminalBlock block)
        {
            if (!Util.IsValid(block) || !(block is IMyAssembler) || CargoSorterSessionComponent.Instance == null)
            {
                return;
            }

            var queued = CargoSorterSessionComponent.Instance.GenerateQueueFromCustomData((IMyAssembler)block);

            MyAPIGateway.Utilities.ShowMissionScreen("Queue Request Results", null, $"{block.DisplayNameText}", queued ? "Queued Custom Data" : "Failed to queue custom data");
        }

        private static void ClearAssemblerQueueItems(IMyTerminalBlock block)
        {
            if (!Util.IsValid(block) || !(block is IMyAssembler))
            {
                return;
            }

            var assembler = (IMyAssembler)block;
            if (assembler.IsQueueEmpty)
            {
                return;
            }

            var queue = assembler.GetQueue();
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                assembler.RemoveQueueItem(i, queue[i].Amount);
            }
        }

        private static void StartQuotaAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null && block is IMyAssembler)
            {
                CargoSorterSessionComponent.Instance.BeginQuotaJob((IMyAssembler)block, ResultsDisplayType.Window);
            }
        }
    }
}