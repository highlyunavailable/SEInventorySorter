using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Interfaces.Terminal;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Definitions;
using Sandbox.Game;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace CargoSorter
{
    public static class CargoTerminalControls
    {
        public static List<IMyTerminalControl> Controls;
        private static bool Done => Controls != null;

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
                MyAPIGateway.TerminalControls.AddControl<IMyCargoContainer>(control);
            }
        }

        internal static void EnsureControlSetup()
        {
            if (Done)
            {
                return;
            }

            Controls = new List<IMyTerminalControl>();
            {
                var control = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyTerminalBlock>("CargoSort_SwapWithCharacterInventory");
                control.Title = MyStringId.GetOrCompute("Swap Inventories");
                control.Tooltip = MyStringId.GetOrCompute("Swap your character's items with the items in this block");
                control.SupportsMultipleBlocks = false;
                control.Visible = CanFitInCharacterInventory;
                control.Action = SwapInventory;
                Controls.Add(control);
            }
        }

        private static void SwapInventory(IMyTerminalBlock block)
        {
            var character = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (!Util.IsValid(character) || !Util.IsValid(block) || block.InventoryCount != 1)
            {
                return;
            }

            var containerInventory = block.GetInventory(0) as MyInventory;
            var characterInventory = character.GetInventory(0) as MyInventory;

            if ((containerInventory?.CurrentVolume ?? MyFixedPoint.Zero) >= (characterInventory?.MaxVolume ?? MyFixedPoint.Zero))
            {
                return;
            }

            var containerItems = new List<MyPhysicalInventoryItem>(containerInventory.GetItems());
            var characterItems = new List<MyPhysicalInventoryItem>(characterInventory.GetItems());

            for (int i = 0; i < Math.Max(containerItems.Count, characterItems.Count); i++)
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

        public static bool CanFitInCharacterInventory(IMyTerminalBlock block)
        {
            var character = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (!Util.IsValid(character) || !Util.IsValid(block) || block.InventoryCount != 1)
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
    }
}