using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

namespace XO.RuntimeDatabase.Authoring
{
    [CreateAssetMenu(fileName = "PrefabTable", menuName = "XO/RuntimeDatabase/Tables/Prefab Table", order = 1)]
    public class PrefabTableSo : RuntimeTableSo<GameObject, EntityPrefabReference>, IRuntimePrefabTable
    {
        public override EntityPrefabReference Convert(GameObject source, Baker<RuntimeDatabaseAuthoring> baker)
        {
#if UNITY_EDITOR
            return source != null ? new EntityPrefabReference(source) : default;
#else
            throw new System.InvalidOperationException("Prefab conversion is an editor baking operation.");
#endif
        }

        public void CollectPrefabs(Baker<RuntimeDatabaseAuthoring> baker, List<PrefabEntryMetadata> outPrefabs)
        {
            if (data == null || data.Length == 0) return;

            for (int i = 0; i < data.Length; i++)
            {
                var entry = data[i];
                if (entry.value != null)
                {
                    baker.DependsOn(entry.value);
                    var entityPrefab = baker.GetEntity(entry.value, TransformUsageFlags.Dynamic);
                    outPrefabs.Add(new PrefabEntryMetadata
                    {
                        PrefabKey = RuntimeDB.Hash32(entry.key),
                        PrefabEntity = entityPrefab
                    });
                }
            }
        }
    }
}
