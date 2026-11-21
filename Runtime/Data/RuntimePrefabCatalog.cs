using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using XO.PopUtils;

namespace XO.RuntimeDatabase
{
    public struct PrefabEntryMetadata
    {
        public uint TableKey;
        public uint PrefabKey;
        public Entity PrefabEntity;
    }

    /// <summary>
    /// Remappable ECS Buffer Element storing baked Entity Prefabs.
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
    /// High-performance Burst-compatible lookup extension methods for DynamicBuffer<PrefabCatalogElement>.
    /// </summary>
    public static class PrefabCatalogExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPrefab(this DynamicBuffer<PrefabCatalogElement> catalog, uint tableKey, uint prefabKey, out Entity prefabEntity)
        {
            int len = catalog.Length;
            for (int i = 0; i < len; i++)
            {
                var elem = catalog[i];
                if (elem.PrefabKey == prefabKey && (tableKey == 0 || elem.TableKey == tableKey))
                {
                    prefabEntity = elem.PrefabEntity;
                    return true;
                }
            }

            // Fallback: search across any table if specific tableKey didn't match
            if (tableKey != 0)
            {
                for (int i = 0; i < len; i++)
                {
                    var elem = catalog[i];
                    if (elem.PrefabKey == prefabKey)
                    {
                        prefabEntity = elem.PrefabEntity;
                        return true;
                    }
                }
            }

            prefabEntity = Entity.Null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetPrefab(this DynamicBuffer<PrefabCatalogElement> catalog, uint prefabKey, out Entity prefabEntity)
        {
            return catalog.TryGetPrefab(0, prefabKey, out prefabEntity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity GetPrefab(this DynamicBuffer<PrefabCatalogElement> catalog, uint tableKey, uint prefabKey)
        {
            return catalog.TryGetPrefab(tableKey, prefabKey, out var prefab) ? prefab : Entity.Null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity GetPrefab(this DynamicBuffer<PrefabCatalogElement> catalog, uint prefabKey)
        {
            return catalog.TryGetPrefab(0, prefabKey, out var prefab) ? prefab : Entity.Null;
        }
    }
}
