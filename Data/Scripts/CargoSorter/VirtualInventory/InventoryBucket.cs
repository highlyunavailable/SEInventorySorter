using System;
using System.Collections.Generic;

namespace CargoSorter
{
    [Flags]
    internal enum InventoryBucketFlags : byte
    {
        None = 0,
        Special = 1 << 0,
        Shuffle = 1 << 1,
    }

    internal class InventoryBucket
    {
        public readonly byte Priority;
        public readonly InventoryBucketFlags Flags;
        public readonly List<InventoryInfo> Inventories;

        public InventoryBucket(byte priority, InventoryBucketFlags flags)
        {
            Priority = priority;
            Flags = flags;
            Inventories = new List<InventoryInfo>();
        }

        public override string ToString()
        {
            return $"Priority: {Priority} - Flags: {Flags}";
        }
    }
}