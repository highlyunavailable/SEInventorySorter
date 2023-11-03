using System.Collections.Generic;
using ParallelTasks;
using VRage.Game.ModAPI;

namespace CargoSorter
{
    public class CargoSorterWorkData : WorkData
    {
        internal readonly List<IMyCubeGrid> Grids = new List<IMyCubeGrid>();
        internal readonly List<InventoryInfo> Inventories = new List<InventoryInfo>();
        internal List<InventoryMovement> MovementData = new List<InventoryMovement>();
    }
}