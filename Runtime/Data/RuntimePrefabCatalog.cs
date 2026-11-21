using System.Runtime.CompilerServices;
using Unity.Entities;

namespace XO.RuntimeDatabase
{
    public struct PrefabEntryMetadata
    {
        public uint TableKey;
        public uint PrefabKey;
        public Entity PrefabEntity;
    }

    /// <summary>
    /// Remappable ECS Buffer Element storing baked Entity Prefabs sorted by (TableKey, PrefabKey).
    /// Unity automatically remaps PrefabEntity from Baking World to Runtime World during SubScene streaming.
    /// Provides zero-latency synchronous ecb.Instantiate access in Burst systems.
    /// </summary>
    [InternalBufferCapacity(32)]
    public struct PrefabCatalogElement : IBufferElementData
    {
        public uint TableKey;
        public uint PrefabKey;
        public Entity PrefabEntity;
    }

    /// <summary>
    /// High-performance Burst-compatible lookup extension methods for DynamicBuffer&lt;PrefabCatalogElement&gt;.
    /// </summary>
    public static class PrefabCatalogExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPrefab(
            this DynamicBuffer<PrefabCatalogElement> catalog,
            uint tableKey,
            uint prefabKey,
            out Entity prefabEntity)
        {
            int len = catalog.Length;
            if (len == 0)
            {
                prefabEntity = Entity.Null;
                return false;
            }

            // O(log N) binary search when tableKey is specified (catalog is sorted by TableKey, then PrefabKey)
            if (tableKey != 0)
            {
                int low = 0;
                int high = len - 1;
                while (low <= high)
                {
                    int mid = (low + high) >> 1;
                    var elem = catalog[mid];
                    if (elem.TableKey == tableKey)
                    {
                        if (elem.PrefabKey == prefabKey)
                        {
                            prefabEntity = elem.PrefabEntity;
                            return true;
                        }
                        if (elem.PrefabKey < prefabKey) low = mid + 1;
                        else high = mid - 1;
                    }
                    else if (elem.TableKey < tableKey)
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid - 1;
                    }
                }
            }

            // Fallback: scan across all tables by prefabKey
            for (int i = 0; i < len; i++)
            {
                var elem = catalog[i];
                if (elem.PrefabKey == prefabKey)
                {
                    prefabEntity = elem.PrefabEntity;
                    return true;
                }
            }

            prefabEntity = Entity.Null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPrefab(
            this DynamicBuffer<PrefabCatalogElement> catalog,
            uint prefabKey,
            out Entity prefabEntity)
        {
            return catalog.TryGetPrefab(0, prefabKey, out prefabEntity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity GetPrefab(
            this DynamicBuffer<PrefabCatalogElement> catalog,
            uint tableKey,
            uint prefabKey)
        {
            return catalog.TryGetPrefab(tableKey, prefabKey, out var prefab) ? prefab : Entity.Null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity GetPrefab(
            this DynamicBuffer<PrefabCatalogElement> catalog,
            uint prefabKey)
        {
            return catalog.TryGetPrefab(0, prefabKey, out var prefab) ? prefab : Entity.Null;
        }
    }
}
