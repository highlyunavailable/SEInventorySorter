﻿using System;

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
        Limited = 1 << 7,
        Special = 1 << 8,
        GasGeneratorOre = 1 << 9,
        AssemblerIngots = 1 << 10,
        RefineryOre = 1 << 11,
        GasTankBottles = 1 << 12,
        ReactorFuel = 1 << 13,
        ConsumableAmmo = 1 << 14,
        SorterItems = 1 << 15,
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