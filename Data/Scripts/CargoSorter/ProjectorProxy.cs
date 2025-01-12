using MultigridProjector.Api;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game.ModAPI;

namespace CargoSorter
{
    internal class ProjectorProxy
    {
        private static MultigridProjectorModAgent mgp;
        private MultigridProjectorModAgent Mgp => mgp ?? (mgp = new MultigridProjectorModAgent());

        private readonly IMyProjector projectorBlock;

        public ProjectorProxy(IMyProjector projector)
        {
            projectorBlock = projector;
        }

        public bool HasBlueprint
        {
            get
            {
                if (!Util.IsValid(projectorBlock))
                {
                    return false;
                }
                if (Mgp?.Available == true)
                {
                    return Mgp.GetSubgridCount(projectorBlock.EntityId) > 0;
                }
                else
                {
                    return projectorBlock.ProjectedGrid != null;
                }
            }
        }

        internal void GetBlocks(List<IMySlimBlock> projectedBlocks)
        {
            if (!Util.IsValid(projectorBlock))
            {
                return;
            }
            if (Mgp?.Available == true)
            {
                for (int subgridIndex = 0; subgridIndex < Mgp.GetSubgridCount(projectorBlock.EntityId); subgridIndex++)
                {
                    var previewGrid = Mgp.GetPreviewGrid(projectorBlock.EntityId, subgridIndex);
                    previewGrid.GetBlocks(projectedBlocks);
                }
            }
            else
            {
                projectorBlock.ProjectedGrid.GetBlocks(projectedBlocks);
            }
        }
    }
}
