using System.Runtime.CompilerServices;
using Unity.Entities;

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

    public struct RuntimeTable<TValue> : IComponentData where TValue : unmanaged
    {
        public BlobAssetReference<RuntimeDatabaseBlob<TValue>> Blob;

        public bool IsCreated => Blob.IsCreated;
        public int TableCount => Blob.IsCreated ? Blob.Value.Tables.Length : 0;
        public int EntryCount => Blob.IsCreated ? Blob.Value.Entries.Length : 0;
        public int Count => TableCount;

        public TValue this[string tableKey, string objectKey] =>
            this[RuntimeDB.Hash32(tableKey), RuntimeDB.Hash32(objectKey)];

        public TValue this[uint tableKey, uint objectKey] =>
            TryGetValue(tableKey, objectKey, out var value) ? value : default;

        public RuntimeTable(BlobAssetReference<RuntimeDatabaseBlob<TValue>> blob)
        {
            Blob = blob;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(uint tableKey, uint objectKey) => TryGetValue(tableKey, objectKey, out _);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(uint objectKey) => TryGetValue(objectKey, out _);

        public bool TryGetValue(string tableKey, string objectKey, out TValue value) =>
            TryGetValue(RuntimeDB.Hash32(tableKey), RuntimeDB.Hash32(objectKey), out value);

        public bool TryGetValue(string objectKey, out TValue value) =>
            TryGetValue(RuntimeDB.Hash32(objectKey), out value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(uint objectKey, out TValue value)
        {
            if (!Blob.IsCreated)
            {
                value = default;
                return false;
            }

            ref var root = ref Blob.Value;
            ref var tables = ref root.Tables;
            ref var entries = ref root.Entries;

            for (int t = 0; t < tables.Length; t++)
            {
                ref var tableMeta = ref tables[t];
                if (BinarySearchEntry(ref entries, tableMeta.StartIndex, tableMeta.Count, objectKey, out value))
                {
                    return true;
                }
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(uint tableKey, uint objectKey, out TValue value)
        {
            if (!Blob.IsCreated)
            {
                value = default;
                return false;
            }

            ref var root = ref Blob.Value;
            int tableIdx = BinarySearchTable(ref root.Tables, tableKey);
            if (tableIdx < 0)
            {
                value = default;
                return false;
            }

            ref var tableMeta = ref root.Tables[tableIdx];
            return BinarySearchEntry(ref root.Entries, tableMeta.StartIndex, tableMeta.Count, objectKey, out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BinarySearchTable(ref BlobArray<TableMetadata> tables, uint tableKey)
        {
            int low = 0;
            int high = tables.Length - 1;

            while (low <= high)
            {
                int mid = (low + high) >> 1;
                uint midKey = tables[mid].TableKey;
                if (midKey == tableKey) return mid;
                if (midKey < tableKey) low = mid + 1;
                else high = mid - 1;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool BinarySearchEntry(
            ref BlobArray<EntryMetadata<TValue>> entries,
            int startIndex,
            int count,
            uint objectKey,
            out TValue value)
        {
            int low = startIndex;
            int high = startIndex + count - 1;

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
    }
}
