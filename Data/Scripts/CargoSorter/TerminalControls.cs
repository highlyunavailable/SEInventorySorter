using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.Game;

namespace CargoSorter.Data.Scripts.CargoSorter
{
    public static class TerminalControls
    {
        private static bool Done = false;

        private static IMyTerminalControlSeparator ShipControllerSeparator;
        private static IMyTerminalControlButton ShipControllerButton;
        private static IMyTerminalAction ShipControllerAction;

        public static IMyTerminalControlButton SortButton
        {
            get
            {
                if (!Done)
                {
                    DoOnce();
                }
                return ShipControllerButton;
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
                return ShipControllerAction;
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


        private static void CreateActions()
        {
            {
                ShipControllerAction = MyAPIGateway.TerminalControls.CreateAction<IMyShipController>("CargoSort_Sort");
                ShipControllerAction.Enabled = IsControlVisible;
                ShipControllerAction.Name = new StringBuilder("Sort Inventory");
                ShipControllerAction.Icon = @"Textures\GUI\Icons\Actions\Reverse.dds";
                ShipControllerAction.ValidForGroups = false;
                ShipControllerAction.InvalidToolbarTypes = new List<MyToolbarType>()
                {
                    MyToolbarType.Character,
                    MyToolbarType.ButtonPanel,
                    MyToolbarType.Seat,
                };
                ShipControllerAction.Action = StartSortAction;
            }
        }

        private static void CreateControls()
        {
            {
                ShipControllerSeparator = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyShipController>(""); // separators don't store the id
                ShipControllerSeparator.SupportsMultipleBlocks = false;
                ShipControllerSeparator.Visible = IsControlVisible;
            }
            {
                ShipControllerButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyShipController>("CargoSort_SortButton");
                ShipControllerButton.Title = MyStringId.GetOrCompute("Sort Inventory");
                ShipControllerButton.Tooltip = MyStringId.GetOrCompute("Sorts the inventory of the current grid and all attached grids");
                ShipControllerButton.SupportsMultipleBlocks = false;
                ShipControllerButton.Visible = IsControlVisible;
                ShipControllerButton.Action = StartSortAction;
            }
        }

        private static void StartSortAction(IMyTerminalBlock block)
        {
            if (Util.IsValid(block) && block is IMyShipController && CargoSorterSessionComponent.Instance != null)
            {
                CargoSorterSessionComponent.Instance.BeginSortJob(block as IMyShipController);
            }
        }
    }
}
