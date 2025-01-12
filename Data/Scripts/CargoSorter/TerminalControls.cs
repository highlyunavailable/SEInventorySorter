using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.Game;

namespace CargoSorter
{
    public static class TerminalControls
    {
        private static bool Done = false;

        public static void DoOnce()
        {
            if (Done)
            {
                return;
            }
            Done = true;

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                CreateControls();
                CreateActions();
            });
        }

        private static bool IsControlVisible(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyShipController;
        private static bool HasQueueReady(IMyTerminalBlock block) => Util.IsValid(block) && (block as IMyAssembler)?.IsQueueEmpty == false;
        private static bool CanMakeQueueFromCustomData(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyAssembler &&
            !block.DisplayNameText.InsensitiveContains(CargoSorterSessionComponent.Instance?.Config?.SpecialContainerKeyword) &&
            !block.DisplayNameText.InsensitiveContains(CargoSorterSessionComponent.Instance?.Config?.LimitedContainerKeyword) &&
            block.CustomData.Contains("[Inventory]");
        private static bool HasProjectedGrid(IMyTerminalBlock block) => Util.IsValid(block) && (block as IMyProjector)?.ProjectedGrid != null;
        private static bool HasQuotaCustomData(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyAssembler &&
            block.CustomData.Contains("[Quota]") && !block.DisplayNameText.InsensitiveContains("[Secondary:");

        private static void CreateActions()
        {
            {
                var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipController>("CargoSort_Sort");
                action.Enabled = IsControlVisible;
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
                MyAPIGateway.TerminalControls.AddAction<IMyCockpit>(action);
                MyAPIGateway.TerminalControls.AddAction<IMyCryoChamber>(action);
                MyAPIGateway.TerminalControls.AddAction<IMyRemoteControl>(action);
            }
            {
                var action = MyAPIGateway.TerminalControls.CreateAction<IMyAssembler>("CargoSort_Quota");
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
                action.Action = controlAction;
                MyAPIGateway.TerminalControls.AddAction<IMyAssembler>(action);
            }
            {
                var action = MyAPIGateway.TerminalControls.CreateAction<IMyAssembler>("CargoSort_ClearQueueAction");
                action.Name = new StringBuilder("Clear Queue");
                action.Icon = @"Textures\GUI\Icons\Actions\Reset.dds";
                action.InvalidToolbarTypes = new List<MyToolbarType>()
                {
                    MyToolbarType.Character,
                    MyToolbarType.ButtonPanel,
                    MyToolbarType.Seat,
                };
                action.Action = ClearAssemblerQueueItems;
                MyAPIGateway.TerminalControls.AddAction<IMyAssembler>(action);
            }
        }

        private static void CreateControls()
        {
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipController>("CargoSort_SortButton");
                control.Title = MyStringId.GetOrCompute("Sort Inventory");
                control.Tooltip = MyStringId.GetOrCompute("Sorts the inventory of the current grid and all attached grids");
                control.SupportsMultipleBlocks = false;
                control.Visible = IsControlVisible;
                control.Action = StartSortButtonAction;
                MyAPIGateway.TerminalControls.AddControl<IMyCockpit>(control);
                MyAPIGateway.TerminalControls.AddControl<IMyCryoChamber>(control);
                MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GeneratePrerequisiteCustomDataFromQueueButton");
                control.Title = MyStringId.GetOrCompute("Make Prerequisite Data");
                control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the prerequisites for this assembler's queue");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasQueueReady;
                control.Action = GeneratePrerequisiteCustomDataFromQueueAction;
                MyAPIGateway.TerminalControls.AddControl<IMyAssembler>(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GenerateCustomDataFromQueueButton");
                control.Title = MyStringId.GetOrCompute("Make Result Data");
                control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the results of this assembler's queue");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasQueueReady;
                control.Action = GenerateResultCustomDataFromQueueAction;
                MyAPIGateway.TerminalControls.AddControl<IMyAssembler>(control);
            }

            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GenerateQueueFromCustomDataButton");
                control.Title = MyStringId.GetOrCompute("Make Queue from Data");
                control.Tooltip = MyStringId.GetOrCompute("Queues up items from the Inventory custom data of this assembler");
                control.SupportsMultipleBlocks = false;
                control.Enabled = CanMakeQueueFromCustomData;
                control.Action = QueueFromCustomDataAction;
                MyAPIGateway.TerminalControls.AddControl<IMyAssembler>(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_ClearQueueButton");
                control.Title = MyStringId.GetOrCompute("Clear Queue");
                control.Tooltip = MyStringId.GetOrCompute("Clears the queues of the selected assemblers");
                control.SupportsMultipleBlocks = true;
                control.Action = ClearAssemblerQueueItems;
                MyAPIGateway.TerminalControls.AddControl<IMyAssembler>(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_BuildToQuotaButton");
                control.Title = MyStringId.GetOrCompute("Build to Quota");
                control.Tooltip = MyStringId.GetOrCompute("Queue up items to match production quotas. Must be a primary group assembler and have quota data.");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasQuotaCustomData;
                control.Action = controlAction;
                MyAPIGateway.TerminalControls.AddControl<IMyAssembler>(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("CargoSort_GenerateCustomDataFromProjectionButton");
                control.Title = MyStringId.GetOrCompute("Make Data from Projection");
                control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data from the projected grid. Paste this into an assembler to queue it!");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasProjectedGrid;
                control.Action = GenerateCustomDataFromProjectionAction;
                MyAPIGateway.TerminalControls.AddControl<IMyProjector>(control);
            }
        }

        private static void StartSortToolbarAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginSortJob(block.CubeGrid, null, ResultsDisplayType.Chat);
            }
        }
        private static void StartSortButtonAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginSortJob(block.CubeGrid, null, ResultsDisplayType.Window);
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
        private static void ClearAssemblerQueueItems(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && block is IMyAssembler)
            {
                var assembler = block as IMyAssembler;
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

        private static void controlAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null && block is IMyAssembler)
            {
                CargoSorterSessionComponent.Instance.BeginQuotaJob(block as IMyAssembler, ResultsDisplayType.Window);
            }
        }

    }
}
