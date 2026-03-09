using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;

namespace InventorySorter.TerminalControls
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
                control.Visible = AllowSwapInventory;
                control.Action = SwapInventory;
                Controls.Add(control);
            }
        }

        private static void SwapInventory(IMyTerminalBlock block)
        {
            if (!Util.IsValid(block) || block.InventoryCount != 1)
            {
                return;
            }

            var character = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (character == null)
            {
                return;
            }

            var containerInventory = block.GetInventory(0) as MyInventory;
            var characterInventory = character.GetInventory(0) as MyInventory;

            if (containerInventory == null ||
                characterInventory == null ||
                containerInventory.CurrentVolume > characterInventory.MaxVolume ||
                characterInventory.CurrentVolume > containerInventory.MaxVolume)
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
                    MyInventory.TransferByUser(characterInventory, containerInventory, excessItem.ItemId);
                }

                if (i >= characterItems.Count)
                {
                    var excessItem = containerItems[i];
                    MyInventory.TransferByUser(containerInventory, characterInventory, excessItem.ItemId);
                }
            }
        }

        public static bool AllowSwapInventory(IMyTerminalBlock block)
        {
            if (!Util.IsValid(block) || block.InventoryCount != 1 || !block.HasLocalPlayerAccess())
            {
                return false;
            }

            var character = MyAPIGateway.Session.LocalHumanPlayer?.Character;
            if (character == null)
            {
                return false;
            }

            // Distance check to prevent remote swapping.
            var blockSphereD = block.WorldVolume;
            blockSphereD.Radius += 5d;
            if (!blockSphereD.Intersects(character.WorldVolume))
            {
                return false;
            }

            var containerInventory = block.GetInventory(0) as MyInventory;
            var characterInventory = character.GetInventory(0) as MyInventory;

            return containerInventory != null &&
                   characterInventory != null &&
                   containerInventory.CurrentVolume <= characterInventory.MaxVolume &&
                   characterInventory.CurrentVolume <= containerInventory.MaxVolume;
        }
    }
}