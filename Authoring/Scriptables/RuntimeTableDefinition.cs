using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using XO.PopUtils;
using XO.RuntimeDatabase;

namespace XO.RuntimeDatabase.Authoring
{
    /// <summary>
    /// Base interface for any runtime database table.
    /// </summary>
    public interface IRuntimeTable
    {
        uint TableKey { get; }
        string TableName { get; }
    }

    /// <summary>
    /// Interface for data tables providing unmanaged flyweight DOTS data.
    /// Used by the authoring baker to dynamically group tables by TDots type.
    /// </summary>
    public interface IRuntimeDataTable : IRuntimeTable
    {
        Type DotsType { get; }
        void BakeGroup(Baker<RuntimeDatabaseAuthoring> baker, Entity entity, List<IRuntimeDataTable> tables);
    }

    /// <summary>
    /// Strongly-typed interface for unmanaged data tables.
    /// </summary>
    public interface IRuntimeTableSo<TDots> : IRuntimeDataTable where TDots : unmanaged
    {
        void GetSubTableData(Baker<RuntimeDatabaseAuthoring> baker, List<uint> outKeys, List<TDots> outValues);
    }

    /// <summary>
    /// Capability interface for tables that provide or procedurally generate Entity Prefabs.
    /// Entities collected here are registered into RuntimePrefabCatalog for zero-latency synchronous ECS spawning.
    /// </summary>
    public interface IRuntimePrefabTable : IRuntimeTable
    {
        void CollectPrefabs(Baker<RuntimeDatabaseAuthoring> baker, List<PrefabEntryMetadata> outPrefabs);
    }

    /// <summary>
    /// Capability interface for tables that bake live entities directly into the SubScene (not prefabs).
    /// </summary>
    public interface IRuntimeEntityBakeTable : IRuntimeTable
    {
        void BakeEntities(Baker<RuntimeDatabaseAuthoring> baker);
    }

    /// <summary>
    /// Base ScriptableObject class for all RuntimeDatabase tables.
    /// </summary>
    public abstract class RuntimeTableSoBase : SerializedScriptableObject, IRuntimeTable
    {
        [Tooltip("Leave empty to use the asset name as the table key")]
        public string tableName;

        public uint TableKey => Pop.Hash32(string.IsNullOrEmpty(tableName) ? name : tableName);
        public string TableName => string.IsNullOrEmpty(tableName) ? name : tableName;

        public virtual void BakeTable(Baker<RuntimeDatabaseAuthoring> baker, Entity entity)
        {
        }
    }

    /// <summary>
    /// Generic ScriptableObject table mapping source data (ScriptableObjects, GameObjects, structs)
    /// into pure unmanaged DOTS flyweight data baked into a RuntimeTable<TDots> blob.
    /// </summary>
    public abstract class RuntimeTableSo<TSource, TDots> : RuntimeTableSoBase, IRuntimeTableSo<TDots>
        where TDots : unmanaged
    {
        public Data<string, TSource>[] data;

        public Type DotsType => typeof(TDots);

        public abstract TDots Convert(TSource source, Baker<RuntimeDatabaseAuthoring> baker);

        public virtual void GetSubTableData(Baker<RuntimeDatabaseAuthoring> baker, List<uint> outKeys, List<TDots> outValues)
        {
            if (data == null || data.Length == 0) return;

            for (int i = 0; i < data.Length; i++)
            {
                outKeys.Add(Pop.Hash32(data[i].key));
                outValues.Add(Convert(data[i].value, baker));
            }
        }

        /// <summary>
        /// Automatically called by RuntimeDatabaseAuthoring to combine ALL tables sharing this TDots type
        /// into a single, high-performance unmanaged RuntimeTable<TDots> singleton.
        /// </summary>
        public void BakeGroup(Baker<RuntimeDatabaseAuthoring> baker, Entity entity, List<IRuntimeDataTable> tables)
        {
            var typedTables = new List<IRuntimeTableSo<TDots>>();
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i] is IRuntimeTableSo<TDots> typed)
                {
                    typedTables.Add(typed);
                }
            }

            if (typedTables.Count == 0) return;

            typedTables.Sort((a, b) => a.TableKey.CompareTo(b.TableKey));

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<RuntimeDatabaseBlob<TDots>>();

            var tablesArray = builder.Allocate(ref root.Tables, typedTables.Count);
            var allEntries = new List<EntryMetadata<TDots>>();

            for (int i = 0; i < typedTables.Count; i++)
            {
                var tableSo = typedTables[i];
                if (tableSo is UnityEngine.Object obj)
                {
                    baker.DependsOn(obj);
                }

                var tempKeys = new List<uint>();
                var tempValues = new List<TDots>();
                tableSo.GetSubTableData(baker, tempKeys, tempValues);

                var subTableEntries = new List<EntryMetadata<TDots>>(tempKeys.Count);
                for (int j = 0; j < tempKeys.Count; j++)
                {
                    subTableEntries.Add(new EntryMetadata<TDots>
                    {
                        ObjectKey = tempKeys[j],
                        Value = tempValues[j]
                    });
                }

                subTableEntries.Sort((a, b) => a.ObjectKey.CompareTo(b.ObjectKey));

                tablesArray[i] = new TableMetadata
                {
                    TableKey = tableSo.TableKey,
                    StartIndex = allEntries.Count,
                    Count = subTableEntries.Count
                };

                allEntries.AddRange(subTableEntries);
            }

            var entriesArray = builder.Allocate(ref root.Entries, allEntries.Count);
            for (int i = 0; i < allEntries.Count; i++)
            {
                entriesArray[i] = allEntries[i];
            }

            var blobRef = builder.CreateBlobAssetReference<RuntimeDatabaseBlob<TDots>>(Allocator.Persistent);
            builder.Dispose();

            baker.AddBlobAsset(ref blobRef, out _);
            baker.AddComponent(entity, new RuntimeTable<TDots>(blobRef));
        }

        public override void BakeTable(Baker<RuntimeDatabaseAuthoring> baker, Entity entity)
        {
            BakeGroup(baker, entity, new List<IRuntimeDataTable> { this });
        }
    }
}
