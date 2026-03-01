using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.Entities;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.ModAPI;
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;

namespace CargoSorter
{
    public class Util
    {
        public static bool IsDedicatedServer =>
            MyAPIGateway.Multiplayer.MultiplayerActive && MyAPIGateway.Utilities.IsDedicated;

        public static bool IsClient =>
            MyAPIGateway.Multiplayer.MultiplayerActive && !MyAPIGateway.Multiplayer.IsServer;

        public static bool IsValid(IMyEntity obj)
        {
            return obj != null && !obj.MarkedForClose && !obj.Closed;
        }

        //
        // Summary:
        //     Removes whitespace from the end. Copied because the real one is prohibited
        public static StringBuilder TrimTrailingWhitespace(StringBuilder sb)
        {
            int num = sb.Length;
            while (num > 0 && (sb[num - 1] == ' ' || sb[num - 1] == '\r' || sb[num - 1] == '\n'))
            {
                num--;
            }

            sb.Length = num;
            return sb;
        }

        public static List<IMyTerminalBlock> CollectBlocksByPattern(IMyCubeGrid rootGrid, string pattern)
        {
            var blocks = new List<IMyTerminalBlock>();
            var collectGroupMembersFromGrid = false;
            pattern = pattern.Trim('"');
            if (pattern.Length > 2)
            {
                collectGroupMembersFromGrid = pattern.StartsWith("G:", StringComparison.OrdinalIgnoreCase);
                pattern = pattern.Remove(0, 2).Trim();
            }

            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(rootGrid);

            if (collectGroupMembersFromGrid)
            {
                var blockGroups = new List<IMyBlockGroup>();
                var tempBlocksIngame = new List<Sandbox.ModAPI.Ingame.IMyTerminalBlock>();
                terminalSystem.GetBlockGroups(blockGroups, group => group.Name.InsensitiveContains(pattern));
                foreach (var group in blockGroups)
                {
                    group.GetBlocks(tempBlocksIngame, b => b.CubeGrid.IsSameConstructAs(rootGrid));
                    blocks.AddRange(tempBlocksIngame.OfType<IMyTerminalBlock>());
                }
            }
            else
            {
                terminalSystem.SearchBlocksOfName(pattern, blocks, b => b.CubeGrid.IsSameConstructAs(rootGrid));
            }

            return blocks;
        }

        public static void CopyCustomData(IMyCubeGrid rootGrid, string sourcePattern, string destPattern)
        {
            var sourceBlocks = CollectBlocksByPattern(rootGrid, sourcePattern);
            var targetBlocks = CollectBlocksByPattern(rootGrid, destPattern);
            targetBlocks.SortNoAlloc((x, y) =>
            {
                var comparison = string.CompareOrdinal(x.DisplayNameText, y.DisplayNameText);
                return comparison == 0 ? x.EntityId.CompareTo(y.EntityId) : comparison;
            });

            if (sourceBlocks.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: No blocks matching the source pattern were found.");
                return;
            }

            if (sourceBlocks.Count > 1)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: Ambiguous source pattern - found {sourceBlocks.Count}, only 1 allowed.");
                return;
            }

            if (targetBlocks.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: No target blocks found.");
                return;
            }

            var sourceCustomData = sourceBlocks[0].CustomData;
            MyAPIGateway.Utilities.ShowMissionScreen("Copy Custom Data?",
                $"From: ",
                sourceBlocks[0].DisplayNameText,
                $"Copying Custom Data to the following targets:\nClick OK to confirm and copy, or close this window/press ESC to cancel\n\n{string.Join("\n", targetBlocks.Select(b => b.DisplayNameText))}",
                clickResult =>
                {
                    if (clickResult != ResultEnum.OK)
                    {
                        return;
                    }

                    foreach (var block in targetBlocks)
                    {
                        if (IsValid(block))
                        {
                            block.CustomData = sourceCustomData;
                        }
                    }
                });
        }

        public static void SplitCustomData(IMyCubeGrid rootGrid, string sourcePattern, string destPattern, string profile)
        {
            var sourceBlocks = CollectBlocksByPattern(rootGrid, sourcePattern);
            var targetBlocks = CollectBlocksByPattern(rootGrid, destPattern);
            targetBlocks.SortNoAlloc((x, y) =>
            {
                var comparison = (x.InventoryCount > 0).CompareTo(y.InventoryCount > 0);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = x.GetInventory(0).MaxVolume.RawValue.CompareTo(y.GetInventory(0).MaxVolume.RawValue);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = string.CompareOrdinal(x.DisplayNameText, y.DisplayNameText);
                return comparison == 0 ? x.EntityId.CompareTo(y.EntityId) : comparison;
            });

            if (sourceBlocks.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: No blocks matching the source pattern were found.");
                return;
            }

            if (sourceBlocks.Count > 1)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: Ambiguous source pattern - found {sourceBlocks.Count}, only 1 allowed.");
                return;
            }

            if (targetBlocks.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: No target blocks found.");
                return;
            }

            var sectionName = profile == null ? "Inventory" : $"Inventory:{profile}";

            var sourceInfo = new InventoryInfo((MyInventory)sourceBlocks[0].GetInventory(0), sectionName);
            if (!sourceInfo.ConfigParseResult.Success)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: Failed to parse source custom data.");
                return;
            }

            if (sourceInfo.Requests.Count == 0)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: Failed to find any requests for the current profile in the custom data.");
            }

            var remainingAmounts = new Dictionary<MyDefinitionId, MyFixedPoint>(sourceInfo.Requests.Count);
            var sumVolume = MyFixedPoint.Zero;
            var sumMass = MyFixedPoint.Zero;
            foreach (var request in sourceInfo.Requests)
            {
                if (request.Flag != RequestFlags.None)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: Cannot split requests with modifiers. Specify whole amounts only.");
                    return;
                }

                remainingAmounts[request.DefinitionId] = request.Amount;

                float mass;
                float volume;
                bool hasIntegralAmounts;
                if (!CargoSorterSessionComponent.TryGetPhysicalItemProperties(request.DefinitionId, out volume, out mass, out hasIntegralAmounts))
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: Invalid item in request: {request.DefinitionId}");
                    return;
                }

                sumVolume += request.Amount * volume;
                sumMass += request.Amount * mass;
            }

            var totalVolume = MyFixedPoint.Zero;
            var totalMass = MyFixedPoint.Zero;
            foreach (var block in targetBlocks)
            {
                if (block.InventoryCount == 0)
                {
                    continue;
                }

                var inventory = (MyInventory)block.GetInventory(0);

                if (sourceInfo.Requests.Count > inventory.MaxItemCount)
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: {block.DisplayNameText} would exceed max item count");
                    return;
                }

                totalVolume = MyFixedPoint.AddSafe(totalVolume, inventory.MaxVolume);
                totalMass = MyFixedPoint.AddSafe(totalMass, inventory.MaxMass);
            }

            if (sumVolume > totalVolume || sumMass > totalMass)
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Error: The requested amounts {sumVolume}/{totalVolume} {sumMass}/{totalMass} cannot fit even after being split!");
                return;
            }

            var targetCustomData = new Dictionary<IMyTerminalBlock, string>(targetBlocks.Count);

            var ini = new MyIni();
            foreach (var block in targetBlocks)
            {
                if (block.InventoryCount == 0)
                {
                    continue;
                }

                var invVolume = block.GetInventory(0).MaxVolume;
                var weight = (double)invVolume / (double)totalVolume;
                var items = new Dictionary<MyDefinitionId, MyFixedPoint>(sourceInfo.Requests.Count);
                foreach (var request in sourceInfo.Requests)
                {
                    var amount = (MyFixedPoint)Math.Floor(weight * (double)remainingAmounts[request.DefinitionId]);
                    if (amount == MyFixedPoint.Zero)
                    {
                        continue;
                    }
                    remainingAmounts[request.DefinitionId] -= amount;
                    items.Add(request.DefinitionId, amount);
                }

                totalVolume -= invVolume;

                targetCustomData.Add(block, InventoryInfo.BuildCustomData(items, false, sectionName, ini));
                ini.Clear();
            }

            MyAPIGateway.Utilities.ShowMissionScreen("Split Custom Data?",
                $"From: ",
                sourceBlocks[0].DisplayNameText,
                $"Splitting custom data over the following targets:\nClick OK to confirm and copy, or close this window/press ESC to cancel\n\n{string.Join("\n", targetCustomData.Keys.Select(b => b.DisplayNameText))}",
                clickResult =>
                {
                    if (clickResult != ResultEnum.OK)
                    {
                        return;
                    }

                    foreach (var blockData in targetCustomData)
                    {
                        if (IsValid(blockData.Key))
                        {
                            blockData.Key.CustomData = blockData.Value;
                        }
                    }
                });
        }
    }

    public static class UtilExtensions
    {
        //
        // Summary:
        //     Removes whitespace from the end. Copied because the real one is prohibited
        public static bool InsensitiveContains(this string inString, string value)
        {
            return inString.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}