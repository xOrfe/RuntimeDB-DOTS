using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace XO.RuntimeDatabase.Authoring
{
    /// <summary>
    /// Central authoring component for the RuntimeDatabase package.
    /// Dynamically groups and bakes:
    /// 1. Unmanaged Data Tables (RuntimeTable&lt;TDots&gt;) with zero hardcoding
    /// 2. Sorted Synchronous Prefab Catalog (PrefabCatalogElement) for direct zero-latency ECS spawning
    /// 3. Direct SubScene Live Entity Baking (IRuntimeEntityBakeTable)
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

                // 1. Bake unmanaged Data Tables (one RuntimeTable<TDots> singleton per distinct TDots type)
                foreach (var kvp in dataTableGroups)
                {
                    var group = kvp.Value;
                    if (group.Count > 0)
                    {
                        group[0].BakeGroup(this, entity, group);
                    }
                }

                // 2. Bake sorted Synchronous Prefab Catalog
                if (prefabTables.Count > 0)
                {
                    BakePrefabCatalog(entity, prefabTables);
                }

                // 3. Bake Live SubScene Entities
                for (int i = 0; i < entityBakeTables.Count; i++)
                {
                    entityBakeTables[i].BakeEntities(this);
                }

                // 4. Standalone custom self-baking tables
                for (int i = 0; i < authoring.tables.Count; i++)
                {
                    var table = authoring.tables[i];
                    if (table != null
                        && table is not IRuntimeDataTable
                        && table is not IRuntimePrefabTable
                        && table is not IRuntimeEntityBakeTable)
                    {
                        table.BakeTable(this, entity);
                    }
                }
            }

            private void BakePrefabCatalog(Entity entity, List<IRuntimePrefabTable> prefabTables)
            {
                var allPrefabs = new List<PrefabEntryMetadata>();
                var subList = new List<PrefabEntryMetadata>();

                for (int i = 0; i < prefabTables.Count; i++)
                {
                    var table = prefabTables[i];
                    subList.Clear();
                    table.CollectPrefabs(this, subList);

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

                // Sort by (TableKey, PrefabKey) to enable O(log N) binary search at runtime
                allPrefabs.Sort((a, b) =>
                {
                    int cmp = a.TableKey.CompareTo(b.TableKey);
                    return cmp != 0 ? cmp : a.PrefabKey.CompareTo(b.PrefabKey);
                });

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
