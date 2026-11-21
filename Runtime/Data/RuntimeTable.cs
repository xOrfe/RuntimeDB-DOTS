using System;
using Unity.Burst;
using Unity.Entities;
using XO.PopUtils;

namespace XO.RuntimeDatabase
{
    public struct TableMetadata
    {
        public uint TableKey;
        public int StartIndex;
        public int Count;
    }

    public struct EntryMetadata<TValue> where TValue : unmanaged
    {
        public uint ObjectKey;
        public TValue Value;
    }

    public struct RuntimeDatabaseBlob<TValue> where TValue : unmanaged
    {
        public BlobArray<TableMetadata> Tables;
        public BlobArray<EntryMetadata<TValue>> Entries;
    }

    [BurstCompile]
    public struct RuntimeTable<TValue> : IComponentData where TValue : unmanaged
    {
        public BlobAssetReference<RuntimeDatabaseBlob<TValue>> Blob;

        public int Count => Blob.IsCreated ? Blob.Value.Tables.Length : 0;
        public TValue this[string tableKey, string objectKey] => this[Pop.Hash32(tableKey), Pop.Hash32(objectKey)];
        public TValue this[uint tableKey, uint objectKey] => TryGetValue(tableKey, objectKey, out var value) ? value : default;

        public RuntimeTable(BlobAssetReference<RuntimeDatabaseBlob<TValue>> blob)
        {
            Blob = blob;
        }

        [BurstCompile]
        public bool Contains(uint tableKey, uint objectKey)
        {
            return TryGetValue(tableKey, objectKey, out _);
        }

        [BurstCompile]
        public bool Contains(uint objectKey)
        {
            return TryGetValue(objectKey, out _);
        }

        [BurstCompile]
        public bool TryGetValue(uint objectKey, out TValue value)
        {
            if (!Blob.IsCreated)
            {
                value = default;
                return false;
            }

            ref var tables = ref Blob.Value.Tables;
            int tableCount = tables.Length;
            if (tableCount == 0)
            {
                value = default;
                return false;
            }

            // Fast path for single-table databases: direct binary search
            if (tableCount == 1)
            {
                ref var tableMeta = ref tables[0];
                ref var entries = ref Blob.Value.Entries;
                int low = tableMeta.StartIndex;
                int high = tableMeta.StartIndex + tableMeta.Count - 1;

                while (low <= high)
                {
                    int mid = (low + high) >> 1;
                    uint midKey = entries[mid].ObjectKey;
                    if (midKey == objectKey)
                    {
                        value = entries[mid].Value;
                        return true;
                    }
                    if (midKey < objectKey) low = mid + 1;
                    else high = mid - 1;
                }

                value = default;
                return false;
            }

            // Search across all tables
            for (int t = 0; t < tableCount; t++)
            {
                ref var tableMeta = ref tables[t];
                ref var entries = ref Blob.Value.Entries;
                int low = tableMeta.StartIndex;
                int high = tableMeta.StartIndex + tableMeta.Count - 1;

                while (low <= high)
                {
                    int mid = (low + high) >> 1;
                    uint midKey = entries[mid].ObjectKey;
                    if (midKey == objectKey)
                    {
                        value = entries[mid].Value;
                        return true;
                    }
                    if (midKey < objectKey) low = mid + 1;
                    else high = mid - 1;
                }
            }

            value = default;
            return false;
        }

        [BurstCompile]
        public bool TryGetValue(uint tableKey, uint objectKey, out TValue value)
        {
            if (!Blob.IsCreated)
            {
                value = default;
                return false;
            }

            ref var tables = ref Blob.Value.Tables;
            int low = 0;
            int high = tables.Length - 1;
            int tableIdx = -1;

            while (low <= high)
            {
                int mid = (low + high) >> 1;
                uint midKey = tables[mid].TableKey;
                if (midKey == tableKey)
                {
                    tableIdx = mid;
                    break;
                }
                else if (midKey < tableKey)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            if (tableIdx == -1)
            {
                value = default;
                return false;
            }

            ref var tableMeta = ref tables[tableIdx];
            ref var entries = ref Blob.Value.Entries;
            low = tableMeta.StartIndex;
            high = tableMeta.StartIndex + tableMeta.Count - 1;

            while (low <= high)
            {
                int mid = (low + high) >> 1;
                uint midKey = entries[mid].ObjectKey;
                if (midKey == objectKey)
                {
                    value = entries[mid].Value;
                    return true;
                }
                else if (midKey < objectKey)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            value = default;
            return false;
        }
    }
}
