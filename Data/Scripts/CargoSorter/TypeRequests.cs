using System;

namespace CargoSorter
{
    [Flags]
    public enum TypeRequests : ushort
    {
        Nothing = 0,
        Components = 1,
        Ingots = 1 << 2,
        Ores = 1 << 3,
        Ammo = 1 << 4,
        Tools = 1 << 5,
        Bottles = 1 << 6,
        Special = 1 << 7,
        GasGeneratorOre = 1 << 8,
        AssemblerIngots = 1 << 9,
        RefineryOre = 1 << 10,
        GasTankBottles = 1 << 11,
        ReactorFuel = 1 << 12,
        WeaponAmmo = 1 << 13,
        SorterItems = 1 << 14,
    }
}