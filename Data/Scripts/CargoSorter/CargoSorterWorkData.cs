using System.Collections.Generic;
using ParallelTasks;
using VRage.Game.ModAPI;

namespace CargoSorter
{
    public class CargoSorterWorkData : WorkData
    {
        internal readonly List<InventoryInfo> Inventories = new List<InventoryInfo>();
        internal readonly List<InventoryMovement> MovementData = new List<InventoryMovement>();
        internal readonly IMyCubeGrid RootGrid;

        public CargoSorterWorkData(IMyCubeGrid cubeGrid)
        {
            RootGrid = cubeGrid;
        }
    }
}