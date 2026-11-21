using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using XO.RuntimeDatabase;

namespace XO.RuntimeDatabase.Authoring
{
    /// <summary>
    /// Central authoring component for the RuntimeDatabase module.
    /// Dynamically groups and bakes:
    /// 1. Unmanaged Data Tables (RuntimeTable<TDots>) with zero hardcoding
    /// 2. Synchronous Prefab Catalog (RuntimePrefabCatalog) for direct zero-latency ECS spawning
    /// 3. Asynchronous Streaming Prefab Tables (EntityPrefabReference)
    /// 4. Direct SubScene Live Entity Baking (IRuntimeEntityBakeTable)
    /// </summary>
    public class RuntimeDatabaseAuthoring : MonoBehaviour
    {
        [Header("Runtime Database Tables")]
        public List<RuntimeTableSoBase> tables = new();

        private class RuntimeDatabaseAuthoringBaker : Baker<RuntimeDatabaseAuthoring>
        {
            public override void Bake(RuntimeDatabaseAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                if (authoring.tables == null || authoring.tables.Count == 0) return;

                // 1. Group tables dynamically by capabilities and TDots type!
                var dataTableGroups = new Dictionary<Type, List<IRuntimeDataTable>>();
                var prefabTables = new List<IRuntimePrefabTable>();
                var entityBakeTables = new List<IRuntimeEntityBakeTable>();

                for (int i = 0; i < authoring.tables.Count; i++)
                {
                    var table = authoring.tables[i];
                    if (table == null) continue;

                    DependsOn(table);

                    if (table is IRuntimeDataTable dataTable)
                    {
                        var type = dataTable.DotsType;
                        if (!dataTableGroups.TryGetValue(type, out var list))
                        {
                            list = new List<IRuntimeDataTable>();
                            dataTableGroups[type] = list;
                        }
                        list.Add(dataTable);
                    }

                    if (table is IRuntimePrefabTable prefabTable)
                    {
                        prefabTables.Add(prefabTable);
                    }

                    if (table is IRuntimeEntityBakeTable entityTable)
                    {
                        entityBakeTables.Add(entityTable);
                    }
                }

                // 2. Bake unmanaged Data Tables (One RuntimeTable<TDots> singleton per distinct TDots type)
                foreach (var kvp in dataTableGroups)
                {
                    var group = kvp.Value;
                    if (group.Count > 0)
                    {
                        group[0].BakeGroup(this, entity, group);
                    }
                }

                // 3. Bake Synchronous Prefab Catalog (Immediate Burst-ready ecb.Instantiate access)
                if (prefabTables.Count > 0)
                {
                    BakePrefabCatalog(entity, prefabTables);
                }

                // 4. Bake Live SubScene Entities (Direct BakeEntity Mode)
                for (int i = 0; i < entityBakeTables.Count; i++)
                {
                    entityBakeTables[i].BakeEntities(this);
                }

                // 5. Standalone legacy self-baking tables
                for (int i = 0; i < authoring.tables.Count; i++)
                {
                    var table = authoring.tables[i];
                    if (table != null && !(table is IRuntimeDataTable) && !(table is IRuntimePrefabTable))
                    {
                        table.BakeTable(this, entity);
                    }
                }
            }

            private void BakePrefabCatalog(Entity entity, List<IRuntimePrefabTable> prefabTables)
            {
                var allPrefabs = new List<PrefabEntryMetadata>();

                for (int i = 0; i < prefabTables.Count; i++)
                {
                    var table = prefabTables[i];
                    var subList = new List<PrefabEntryMetadata>();
                    table.CollectPrefabs(this, subList);

                    if (subList.Count == 0) continue;

                    for (int j = 0; j < subList.Count; j++)
                    {
                        allPrefabs.Add(new PrefabEntryMetadata
                        {
                            TableKey = table.TableKey,
                            PrefabKey = subList[j].PrefabKey,
                            PrefabEntity = subList[j].PrefabEntity
                        });
                    }
                }

                if (allPrefabs.Count == 0) return;

                var catalogBuffer = AddBuffer<PrefabCatalogElement>(entity);
                for (int i = 0; i < allPrefabs.Count; i++)
                {
                    catalogBuffer.Add(new PrefabCatalogElement
                    {
                        TableKey = allPrefabs[i].TableKey,
                        PrefabKey = allPrefabs[i].PrefabKey,
                        PrefabEntity = allPrefabs[i].PrefabEntity
                    });
                }
            }
        }
    }
}
