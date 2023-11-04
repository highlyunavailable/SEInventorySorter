using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace CargoSorter
{
    public class GridConnectorTree
    {
        public GridConnectorTree Root { get; private set; }
        public readonly Dictionary<IMyShipConnector, GridConnectorTree> Branches = new Dictionary<IMyShipConnector, GridConnectorTree>();
        public readonly HashSet<IMyCubeGrid> Grids = new HashSet<IMyCubeGrid>();

        private GridConnectorTree(IMyCubeGrid root, GridConnectorTree parent)
        {
            Root = parent;
            root.GetGridGroup(GridLinkTypeEnum.Mechanical).GetGrids(Grids);

            foreach (IMyCubeGrid grid in Grids)
            {
                foreach (var connector in grid.GetFatBlocks<IMyShipConnector>())
                {
                    if (connector?.Status != Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected)
                    {
                        continue;
                    }

                    if (parent != null && parent.Grids.Contains(connector.OtherConnector.CubeGrid))
                    {
                        continue;
                    }

                    if (Grids.Contains(connector.OtherConnector.CubeGrid))
                    {
                        continue;
                    }

                    if (!Branches.ContainsKey(connector))
                    {
                        GridConnectorTree connectedTree;
                        connectedTree = FindInTreeRecursive(connector.OtherConnector.CubeGrid);
                        if (connectedTree == null)
                        {
                            connectedTree = new GridConnectorTree(connector.OtherConnector.CubeGrid, this);
                        }
                        Branches[connector] = connectedTree;
                    }
                }
            }
        }

        private GridConnectorTree FindInTreeRecursive(IMyCubeGrid otherCubeGrid)
        {
            return FindInTreeUpward(otherCubeGrid) ?? FindInTreeDownward(otherCubeGrid);
        }

        private GridConnectorTree FindInTreeUpward(IMyCubeGrid otherCubeGrid)
        {
            var result = Root?.FindInTreeUpward(otherCubeGrid);
            return result ?? FindInTree(otherCubeGrid);
        }

        private GridConnectorTree FindInTreeDownward(IMyCubeGrid otherCubeGrid)
        {
            GridConnectorTree result = FindInTree(otherCubeGrid);
            if (result == null)
            {
                foreach (var existingConnection in Branches)
                {
                    result = existingConnection.Value.FindInTreeDownward(otherCubeGrid);
                    if (result != null)
                    {
                        break;
                    }
                }
            }
            return result;
        }

        private GridConnectorTree FindInTree(IMyCubeGrid otherCubeGrid)
        {
            return Grids.Contains(otherCubeGrid) ? this : null;
        }

        public GridConnectorTree(IMyCubeGrid root) : this(root, null) { }

        public HashSet<GridConnectorTree> GatherRecursive(Func<IMyShipConnector, bool> filter = null)
        {
            HashSet<GridConnectorTree> nodes = new HashSet<GridConnectorTree>();
            GatherRecursiveInternal(nodes, filter);
            return nodes;
        }

        private void GatherRecursiveInternal(HashSet<GridConnectorTree> nodes, Func<IMyShipConnector, bool> filter)
        {
            nodes.Add(this);
            foreach (var branch in Branches)
            {
                if (filter == null || filter(branch.Key))
                {
                    if (nodes.Contains(branch.Value))
                    {
                        continue;
                    }
                    branch.Value.GatherRecursiveInternal(nodes, filter);
                }
            }
        }
        public static HashSet<IMyCubeGrid> GatherGrids(HashSet<GridConnectorTree> nodes)
        {
            HashSet<IMyCubeGrid> grids = new HashSet<IMyCubeGrid>();
            foreach (var node in nodes) { grids.UnionWith(node.Grids); }
            return grids;
        }
    }
}
