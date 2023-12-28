using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI;
using Sandbox.Definitions;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.Game;
using VRage;
using System.Linq;
using Sandbox.Engine.Utils;
using System;

namespace CargoSorter.Data.Scripts.CargoSorter
{
    public static class TerminalControls
    {
        private static bool Done = false;

        private static IMyTerminalControlButton SortInventoryButton;
        private static IMyTerminalAction SortInventoryAction;
        private static IMyTerminalControlButton GeneratePrerequisiteCustomDataFromQueueButton;
        private static IMyTerminalControlButton GenerateResultCustomDataFromQueueButton;
        private static IMyTerminalControlButton GenerateQueueFromCustomDataButton;
        private static IMyTerminalControlButton GenerateCustomDataFromProjectionButton;

        public static IMyTerminalControlButton SortButton
        {
            get
            {
                if (!Done)
                {
                    DoOnce();
                }
                return SortInventoryButton;
            }
        }
        public static IMyTerminalAction SortAction
        {
            get
            {
                if (!Done)
                {
                    DoOnce();
                }
                return SortInventoryAction;
            }
        }
        public static IMyTerminalControlButton GeneratePrerequisiteCustomDataFromQueue
        {
            get
            {
                if (!Done)
                {
                    DoOnce();
                }
                return GeneratePrerequisiteCustomDataFromQueueButton;
            }
        }
        public static IMyTerminalControlButton GenerateResultCustomDataFromQueue
        {
            get
            {
                if (!Done)
                {
                    DoOnce();
                }
                return GenerateResultCustomDataFromQueueButton;
            }
        }

        public static IMyTerminalControlButton GenerateQueueFromCustomData
        {
            get
            {
                if (!Done)
                {
                    DoOnce();
                }
                return GenerateQueueFromCustomDataButton;
            }
        }

        public static IMyTerminalControlButton GenerateCustomDataFromProjection
        {
            get
            {
                if (!Done)
                {
                    DoOnce();
                }
                return GenerateCustomDataFromProjectionButton;
            }
        }

        public static void DoOnce()
        {
            if (Done)
            {
                return;
            }
            Done = true;

            // these are all the options and they're not all required so use only what you need.
            CreateControls();
            CreateActions();
        }

        private static bool IsControlVisible(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyShipController;

        private static bool HasQueueReady(IMyTerminalBlock block) => Util.IsValid(block) && (block as IMyAssembler)?.IsQueueEmpty == false;
        private static bool CanMakeQueueFromCustomData(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyAssembler &&
            !block.DisplayNameText.InsensitiveContains(CargoSorterSessionComponent.Instance?.Config?.SpecialContainerKeyword) &&
            !block.DisplayNameText.InsensitiveContains(CargoSorterSessionComponent.Instance?.Config?.LimitedContainerKeyword) &&
            block.CustomData.Contains("[Inventory]");
        private static bool HasProjectedGrid(IMyTerminalBlock block) => Util.IsValid(block) && (block as IMyProjector)?.ProjectedGrid != null;

        private static void CreateActions()
        {
            {
                SortInventoryAction = MyAPIGateway.TerminalControls.CreateAction<IMyShipController>("CargoSort_Sort");
                SortInventoryAction.Enabled = IsControlVisible;
                SortInventoryAction.Name = new StringBuilder("Sort Inventory");
                SortInventoryAction.Icon = @"Textures\GUI\Icons\Actions\Reverse.dds";
                SortInventoryAction.ValidForGroups = false;
                SortInventoryAction.InvalidToolbarTypes = new List<MyToolbarType>()
                {
                    MyToolbarType.Character,
                    MyToolbarType.ButtonPanel,
                    MyToolbarType.Seat,
                };
                SortInventoryAction.Action = StartSortToolbarAction;
            }
        }

        private static void CreateControls()
        {
            {
                SortInventoryButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipController>("CargoSort_SortButton");
                SortInventoryButton.Title = MyStringId.GetOrCompute("Sort Inventory");
                SortInventoryButton.Tooltip = MyStringId.GetOrCompute("Sorts the inventory of the current grid and all attached grids");
                SortInventoryButton.SupportsMultipleBlocks = false;
                SortInventoryButton.Visible = IsControlVisible;
                SortInventoryButton.Action = StartSortButtonAction;
            }
            {
                GeneratePrerequisiteCustomDataFromQueueButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GeneratePrerequisiteCustomDataFromQueueButton");
                GeneratePrerequisiteCustomDataFromQueueButton.Title = MyStringId.GetOrCompute("Make Prerequisite Data");
                GeneratePrerequisiteCustomDataFromQueueButton.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the prerequisites for this assembler's queue");
                GeneratePrerequisiteCustomDataFromQueueButton.SupportsMultipleBlocks = false;
                GeneratePrerequisiteCustomDataFromQueueButton.Enabled = HasQueueReady;
                GeneratePrerequisiteCustomDataFromQueueButton.Action = GeneratePrerequisiteCustomDataFromQueueAction;
            }
            {
                GenerateResultCustomDataFromQueueButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GenerateCustomDataFromQueueButton");
                GenerateResultCustomDataFromQueueButton.Title = MyStringId.GetOrCompute("Make Result Data");
                GenerateResultCustomDataFromQueueButton.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the results of this assembler's queue");
                GenerateResultCustomDataFromQueueButton.SupportsMultipleBlocks = false;
                GenerateResultCustomDataFromQueueButton.Enabled = HasQueueReady;
                GenerateResultCustomDataFromQueueButton.Action = GenerateResultCustomDataFromQueueAction;
            }
            {
                GenerateQueueFromCustomDataButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GenerateQueueFromCustomDataButton");
                GenerateQueueFromCustomDataButton.Title = MyStringId.GetOrCompute("Make Queue from Data");
                GenerateQueueFromCustomDataButton.Tooltip = MyStringId.GetOrCompute("Queues up items from the Inventory custom data of this assembler");
                GenerateQueueFromCustomDataButton.SupportsMultipleBlocks = false;
                GenerateQueueFromCustomDataButton.Enabled = CanMakeQueueFromCustomData;
                GenerateQueueFromCustomDataButton.Action = QueueFromCustomDataAction;
            }
            {
                GenerateCustomDataFromProjectionButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("CargoSort_GenerateCustomDataFromProjectionButton");
                GenerateCustomDataFromProjectionButton.Title = MyStringId.GetOrCompute("Make Data from Projection");
                GenerateCustomDataFromProjectionButton.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data from the projected grid. Paste this into an assembler to queue it!");
                GenerateCustomDataFromProjectionButton.SupportsMultipleBlocks = false;
                GenerateCustomDataFromProjectionButton.Enabled = HasProjectedGrid;
                GenerateCustomDataFromProjectionButton.Action = GenerateCustomDataFromProjectionAction;
            }
        }

        private static void StartSortToolbarAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginSortJob(block.CubeGrid, ResultsDisplayType.Chat);
            }
        }
        private static void StartSortButtonAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginSortJob(block.CubeGrid, ResultsDisplayType.Window);
            }
        }

        private static void GeneratePrerequisiteCustomDataFromQueueAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && block is IMyAssembler && CargoSorterSessionComponent.Instance != null)
            {
                var data = CargoSorterSessionComponent.Instance.GeneratePrerequisiteCustomDataFromQueue(block as IMyAssembler);
                MyAPIGateway.Utilities.ShowMissionScreen("Generated Custom Data", $"{block.DisplayNameText}", " Queue Prerequisites", data, (clickResult) =>
                {
                    if (!string.IsNullOrWhiteSpace(data) && clickResult == ResultEnum.OK)
                    {
                        MyClipboardHelper.SetClipboard(data);
                    }
                }, !string.IsNullOrWhiteSpace(data) ? "Copy to Clipboard" : null);
            }
        }

        private static void GenerateResultCustomDataFromQueueAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && block is IMyAssembler && CargoSorterSessionComponent.Instance != null)
            {
                var data = CargoSorterSessionComponent.Instance.GenerateResultCustomDataFromQueue(block as IMyAssembler);
                MyAPIGateway.Utilities.ShowMissionScreen("Generated Custom Data", $"{block.DisplayNameText}", " Queue Results", data, (clickResult) =>
                {
                    if (!string.IsNullOrWhiteSpace(data) && clickResult == ResultEnum.OK)
                    {
                        MyClipboardHelper.SetClipboard(data);
                    }
                }, !string.IsNullOrWhiteSpace(data) ? "Copy to Clipboard" : null);
            }
        }

        private static void QueueFromCustomDataAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && block is IMyAssembler && CargoSorterSessionComponent.Instance != null)
            {
                var queued = CargoSorterSessionComponent.Instance.GenerateQueueFromCustomData(block as IMyAssembler);

                MyAPIGateway.Utilities.ShowMissionScreen("Queue Request Results", null, $"{block.DisplayNameText}", queued ? "Queued Custom Data" : "Failed to queue custom data");
            }
        }

        private static void GenerateCustomDataFromProjectionAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && block is IMyProjector && CargoSorterSessionComponent.Instance != null)
            {
                var data = CargoSorterSessionComponent.Instance.GenerateCustomDataFromProjector(block as IMyProjector);
                MyAPIGateway.Utilities.ShowMissionScreen("Generated Custom Data", $"{block.DisplayNameText}", " Grid Components", data, (clickResult) =>
                {
                    if (!string.IsNullOrWhiteSpace(data) && clickResult == ResultEnum.OK)
                    {
                        MyClipboardHelper.SetClipboard(data);
                    }
                }, !string.IsNullOrWhiteSpace(data) ? "Copy to Clipboard" : null);
            }
        }
    }
}
