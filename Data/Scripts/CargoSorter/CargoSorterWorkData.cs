using System;
using System.Collections.Generic;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.ModAPI;
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
        internal readonly DateTime StartTime = DateTime.UtcNow;
        internal readonly ResultsDisplayType ResultsType;
        internal readonly List<InventoryMovement> MovementData = new List<InventoryMovement>();
        internal readonly IMyCubeGrid RootGrid;
        internal readonly bool ConstructOnly;
        internal readonly Dictionary<MyDefinitionId, MyFixedPoint> AvailableForDistribution = new Dictionary<MyDefinitionId, MyFixedPoint>();
        internal readonly Dictionary<ValueTuple<TypeRequests, MyDefinitionId>, int> RequestTypeCount = new Dictionary<ValueTuple<TypeRequests, MyDefinitionId>, int>();
        internal readonly Dictionary<MyDefinitionId, List<ExcessInfo>> ExcessPools = new Dictionary<MyDefinitionId, List<ExcessInfo>>();
        internal readonly Dictionary<MyDefinitionId, List<InventoryBucket>> TypeBuckets = new Dictionary<MyDefinitionId, List<InventoryBucket>>();
        internal readonly string SectionName;

        public CargoSorterWorkData(IMyCubeGrid cubeGrid, string profile,  bool constructOnly, ResultsDisplayType resultsDisplayType = ResultsDisplayType.Chat)
        {
            RootGrid = cubeGrid;
            ConstructOnly = constructOnly;
            ResultsType = resultsDisplayType;
            SectionName = string.IsNullOrEmpty(profile) ? "Inventory" : $"Inventory:{profile}";
        }
    }

    public class QuotaManagerWorkData : WorkData
    {
        internal readonly ResultsDisplayType ResultsType;
        internal readonly IMyAssembler Block;
        internal readonly ProductionQuotaInfo QuotaInfo;
        internal readonly List<AssemblerQuotaInfo> GroupAssemblers = new List<AssemblerQuotaInfo>();
        internal readonly HashSet<IMyAssembler> MarkedForDisassembly = new HashSet<IMyAssembler>();
        internal readonly HashSet<MyDefinitionId> ActiveAssembling = new HashSet<MyDefinitionId>();
        internal readonly HashSet<MyDefinitionId> ActiveDisassembling = new HashSet<MyDefinitionId>();
        internal readonly Dictionary<MyDefinitionId, MyFixedPoint> MissingItems = new Dictionary<MyDefinitionId, MyFixedPoint>();
        internal readonly Dictionary<MyDefinitionId, List<AssemblerQuotaInfo>> ItemAvailableAssemblers = new Dictionary<MyDefinitionId, List<AssemblerQuotaInfo>>();

        public QuotaManagerWorkData(IMyAssembler block, ProductionQuotaInfo quotaInfo, ResultsDisplayType resultsType = ResultsDisplayType.Chat)
        {
            Block = block;
            QuotaInfo = quotaInfo;
            ResultsType = resultsType;
            if (quotaInfo.QuotaItems == null)
            {
                return;
            }
            foreach (var item in quotaInfo.QuotaItems)
            {
                MissingItems.Add(item.ItemId, item.Amount);
            }
        }
    }
    public class AssemblerQuotaInfo
    {
        public readonly IMyAssembler Block;
        public bool AllowAssembly;
        public bool AllowDisassembly;
        public bool ClearQueue;
        public readonly float AssemblerWeight;

        public AssemblerQuotaInfo(IMyAssembler block)
        {
            Block = block;
            AllowAssembly = true;
            AllowDisassembly = false;
            ClearQueue = false;
            var def = MyDefinitionManager.Static.GetDefinition(block.BlockDefinition) as MyAssemblerDefinition;
            AssemblerWeight = def == null ? 0f : def.AssemblySpeed + block.UpgradeValues.GetValueOrDefault("Productivity", 0f);
        }
    }
}