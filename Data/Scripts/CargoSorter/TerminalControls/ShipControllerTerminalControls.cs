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
    public static class ShipControllerTerminalControls
    {
        public static List<IMyTerminalControl> Controls;
        public static List<IMyTerminalAction> Actions;
        private static bool Done => Controls != null && Actions != null;
        private static bool _controlsCockpitAdded;
        private static bool _controlsCryoChamberAdded;
        private static bool _controlsRemoteControlAdded;

        internal static void EnsureControlCockpitAdded()
        {
            if (_controlsCockpitAdded)
            {
                return;
            }

            EnsureControlSetup();

            _controlsCockpitAdded = true;

            foreach (var control in Controls)
            {
                MyAPIGateway.TerminalControls.AddControl<IMyCockpit>(control);
            }

            foreach (var action in Actions)
            {
                MyAPIGateway.TerminalControls.AddAction<IMyCockpit>(action);
            }
        }

        internal static void EnsureControlCryoChamberAdded()
        {
            if (_controlsCryoChamberAdded)
            {
                return;
            }

            EnsureControlSetup();

            _controlsCryoChamberAdded = true;

            foreach (var control in Controls)
            {
                MyAPIGateway.TerminalControls.AddControl<IMyCryoChamber>(control);
            }

            foreach (var action in Actions)
            {
                MyAPIGateway.TerminalControls.AddAction<IMyCryoChamber>(action);
            }
        }

        internal static void EnsureControlRemoteAdded()
        {
            if (_controlsRemoteControlAdded)
            {
                return;
            }

            EnsureControlSetup();

            _controlsRemoteControlAdded = true;

            foreach (var control in Controls)
            {
                MyAPIGateway.TerminalControls.AddControl<IMyRemoteControl>(control);
            }

            foreach (var action in Actions)
            {
                MyAPIGateway.TerminalControls.AddAction<IMyRemoteControl>(action);
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
                var action = MyAPIGateway.TerminalControls.CreateAction<IMyShipController>("CargoSort_SortConstruct");
                action.Enabled = IsControlVisible;
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
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipController>("CargoSort_SortConstructButton");
                control.Title = MyStringId.GetOrCompute("Sort Construct Inventory");
                control.Tooltip = MyStringId.GetOrCompute("Sorts the inventory of the current grid and all physically attached sub-grids");
                control.SupportsMultipleBlocks = false;
                control.Visible = IsControlVisible;
                control.Action = StartSortConstructButtonAction;
                Controls.Add(control);
            }

            //MyLog.Default.WriteLineAndConsole($"CargoSort: Added ship controller controls: Done: {Done}");
        }

        public static bool IsControlVisible(IMyTerminalBlock block) => Util.IsValid(block) && block is IMyShipController;

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
    }
}