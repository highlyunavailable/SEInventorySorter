using System;
using System.Collections.Generic;
using ParallelTasks;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;

namespace CargoSorter
{
    public enum ResultsDisplayType : byte
    {
        Chat,
        Window,
        None,
    }
    public class CargoSorterWorkData : WorkData
    {
        internal readonly ResultsDisplayType ResultsType;
        internal readonly List<InventoryInfo> Inventories = new List<InventoryInfo>();
        internal readonly List<InventoryMovement> MovementData = new List<InventoryMovement>();
        internal readonly IMyCubeGrid RootGrid;
        internal readonly Dictionary<MyDefinitionId, MyFixedPoint> AvailableForDistribution = new Dictionary<MyDefinitionId, MyFixedPoint>();
        internal readonly Dictionary<ValueTuple<TypeRequests, MyDefinitionId>, int> RequestTypeCount = new Dictionary<ValueTuple<TypeRequests, MyDefinitionId>, int>();

        public CargoSorterWorkData(IMyCubeGrid cubeGrid, ResultsDisplayType resultsDisplayType = ResultsDisplayType.Chat)
        {
            RootGrid = cubeGrid;
            ResultsType = resultsDisplayType;
        }
    }
}