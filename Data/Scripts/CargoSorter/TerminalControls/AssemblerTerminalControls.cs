using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage.Game;

namespace CargoSorter
{
    public static class AssemblerTerminalControls
    {
        public static List<IMyTerminalControl> Controls;
        public static List<IMyTerminalAction> Actions;
        private static bool Done => Controls != null && Actions != null;
        private static bool _controlsAdded;

        internal static void EnsureControlAdded()
        {
            if (_controlsAdded)
            {
                return;
            }

            EnsureControlSetup();

            _controlsAdded = true;

            foreach (var control in Controls)
            {
                MyAPIGateway.TerminalControls.AddControl<IMyAssembler>(control);
            }

            foreach (var action in Actions)
            {
                MyAPIGateway.TerminalControls.AddAction<IMyAssembler>(action);
            }
        }

        internal static void EnsureControlSetup()
        {
            if (Done)
            {
                return;
            }

            Actions = new List<IMyTerminalAction>();
            Controls = new List<IMyTerminalControl>();

            // Set up actions
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
                action.Action = StartQuotaAction;
                Actions.Add(action);
            }
            {
                var action = MyAPIGateway.TerminalControls.CreateAction<IMyAssembler>("CargoSort_ClearQueueAction");
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
                Actions.Add(action);
            }

            // Set up controls
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_ClearQueueButton");
                control.Title = MyStringId.GetOrCompute("Clear Queue");
                control.Tooltip = MyStringId.GetOrCompute("Clears the queues of the selected assemblers");
                control.SupportsMultipleBlocks = true;
                control.Action = ClearAssemblerQueueItems;
                Controls.Add(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_BuildToQuotaButton");
                control.Title = MyStringId.GetOrCompute("Build to Quota");
                control.Tooltip = MyStringId.GetOrCompute("Queue up items to match production quotas. Must be a primary group assembler and have quota data.");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasQuotaCustomData;
                control.Action = StartQuotaAction;
                Controls.Add(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GeneratePrerequisiteCustomDataFromQueueButton");
                control.Title = MyStringId.GetOrCompute("Make Prerequisite Data");
                control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the prerequisites for this assembler's queue");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasQueueReady;
                control.Action = GeneratePrerequisiteCustomDataFromQueueAction;
                Controls.Add(control);
            }
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GenerateCustomDataFromQueueButton");
                control.Title = MyStringId.GetOrCompute("Make Result Data");
                control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data that can be pasted into a Special/Limited container to fill it with the results of this assembler's queue");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasQueueReady;
                control.Action = GenerateResultCustomDataFromQueueAction;
                Controls.Add(control);
            }

            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyAssembler>("CargoSort_GenerateQueueFromCustomDataButton");
                control.Title = MyStringId.GetOrCompute("Make Queue from Data");
                control.Tooltip = MyStringId.GetOrCompute("Queues up items from the Inventory custom data of this assembler");
                control.SupportsMultipleBlocks = false;
                control.Enabled = CanMakeQueueFromCustomData;
                control.Action = QueueFromCustomDataAction;
                Controls.Add(control);
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Added assembler controls: Done: {Done}");
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

        private static void StartQuotaAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && Util.IsValid(block.CubeGrid) && CargoSorterSessionComponent.Instance != null && block is IMyAssembler)
            {
                CargoSorterSessionComponent.Instance.BeginQuotaJob((IMyAssembler)block, ResultsDisplayType.Window);
            }
        }
    }
}