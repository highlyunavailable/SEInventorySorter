using VRage;

namespace InventorySorter.VirtualInventory
{
    public struct ExcessInfo
    {
        public readonly InventoryInfo Inventory;
        public MyFixedPoint Amount;
        public ExcessInfo(InventoryInfo inventory, MyFixedPoint amount) : this()
        {
            Inventory = inventory;
            Amount = amount;
        }
    }
}
