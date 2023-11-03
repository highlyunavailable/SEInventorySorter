using VRage;
using VRage.Game;
using VRage.Game.ModAPI;

namespace CargoSorter
{
    public struct InventoryMovement
    {
        public readonly InventoryInfo Source;
        public readonly InventoryInfo Destination;
        public readonly MyDefinitionId Item;
        public readonly MyFixedPoint Amount;
        public readonly MyFixedPoint Volume;
        public readonly MyFixedPoint Mass;

        public InventoryMovement(InventoryInfo inv, InventoryInfo nextInv, MyDefinitionId item, MyFixedPoint amount, MyFixedPoint volume, MyFixedPoint mass) : this()
        {
            Source = inv;
            Destination = nextInv;
            Item = item;
            Amount = amount;
            Volume = volume;
            Mass = mass;
        }

        public override string ToString()
        {
            return $"{(Source?.RealInventory?.Entity as IMyCubeBlock)?.DisplayNameText ?? "(NULL)"} -> {(Destination?.RealInventory?.Entity as IMyCubeBlock)?.DisplayNameText ?? "(NULL)"} : {Item} - {Amount} V: {Volume * 1000}m3 M: {Mass}kg";
        }
    }
}