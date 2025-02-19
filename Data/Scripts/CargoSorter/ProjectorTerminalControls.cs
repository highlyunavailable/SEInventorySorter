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

namespace CargoSorter.Data.Scripts.CargoSorter
{
    public static class ProjectorTerminalControls
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
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyProjector>("CargoSort_GenerateCustomDataFromProjectionButton");
                control.Title = MyStringId.GetOrCompute("Make Data from Projection");
                control.Tooltip = MyStringId.GetOrCompute("Makes sorter custom data from the projected grid. Paste this into an assembler to queue it!");
                control.SupportsMultipleBlocks = false;
                control.Enabled = HasProjectedGrid;
                control.Action = GenerateCustomDataFromProjectionAction;
                Controls.Add(control);
            }
            MyLog.Default.WriteLineAndConsole($"CargoSort: Added projector controls: Done: {Done}");
        }
        public static bool HasProjectedGrid(IMyTerminalBlock block) => Util.IsValid(block) && (block as IMyProjector)?.ProjectedGrid != null;
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
