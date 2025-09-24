using System;

namespace CargoSorter
{
    [Flags]
    public enum TypeRequests : uint
    {
        Nothing = 0,
        Components = 1,
        Ingots = 1 << 2,
        Ores = 1 << 3,
        Ammo = 1 << 4,
        Tools = 1 << 5,
        Bottles = 1 << 6,
        Consumables = 1 << 7,
        Ingredients = 1 << 8,
        Limited = 1 << 14,
        Special = 1 << 15,
        GasGeneratorOre = 1 << 25,
        AssemblerIngots = 1 << 26,
        RefineryOre = 1 << 27,
        GasTankBottles = 1 << 28,
        ReactorFuel = 1 << 29,
        ConsumableAmmo = 1 << 30,
        SorterItems = (uint)1 << 31,
    }

    [Flags]
    public enum RequestValidationStatus : byte
    {
        Valid = 0,
        InvalidItem = 1,
        TooMuchVolume = 1 << 2,
        InvalidCustomData = 1 << 3,
        InvalidCount = 1 << 4,
    }
}