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

namespace CargoSorter.Data.Scripts.CargoSorter
{
    public static class ShipControllerTerminalControls
    {
        public static List<IMyTerminalControl> Controls;
        public static List<IMyTerminalAction> Actions;
        private static bool Done => Controls != null && Actions != null;
        internal static void DoOnce()
        {
            if (Done)
            {
                return;
            }

            Actions = new List<IMyTerminalAction>();
            Controls = new List<IMyTerminalControl>();

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
                Actions.Add(action);
            }

            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipController>("CargoSort_SortButton");
                control.Title = MyStringId.GetOrCompute("Sort Inventory");
                control.Tooltip = MyStringId.GetOrCompute("Sorts the inventory of the current grid and all attached grids");
                control.SupportsMultipleBlocks = false;
                control.Visible = IsControlVisible;
                control.Action = StartSortButtonAction;
                Controls.Add(control);
            }

            MyLog.Default.WriteLineAndConsole($"CargoSort: Added ship controller controls: Done: {Done}");
        }

        public static bool IsControlVisible(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyShipController;
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
    }
}
