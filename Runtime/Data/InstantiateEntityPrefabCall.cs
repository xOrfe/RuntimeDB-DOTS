using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using XO.PopUtils;
namespace XO.RuntimeDatabase
{
    public delegate void InstantiateEntityPrefabCallback(in EntityCommandBuffer ecb, in Entity instantiatedEntity, int index);

    public readonly struct InstantiateEntityPrefabCall : IComponentData
    {
        public readonly uint TableKey;
        public readonly uint Key;

        public readonly int InstantiateCount;
        public readonly Entity Parent;
        public readonly LocalTransform Transform;
        public FunctionPointer<InstantiateEntityPrefabCallback>? OnInstantiate { get; }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount,
            InstantiateEntityPrefabCallback onInstantiate = null)
        {
            TableKey = tableKey;
            Key = key;
            InstantiateCount = instantiateCount;
            Parent = Entity.Null;
            Transform = Pop.NewLocalTransform();
            OnInstantiate = onInstantiate == null ? null : BurstCompiler.CompileFunctionPointer(onInstantiate);
        }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount, Entity parent,
            InstantiateEntityPrefabCallback onInstantiate = null)
        {
            TableKey = tableKey;
            Key = key;
            InstantiateCount = instantiateCount;
            Parent = parent;
            Transform = Pop.NewLocalTransform();
            OnInstantiate = onInstantiate == null ? null : BurstCompiler.CompileFunctionPointer(onInstantiate);
        }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount, LocalTransform transform,
            InstantiateEntityPrefabCallback onInstantiate = null)
        {
            TableKey = tableKey;
            Key = key;
            InstantiateCount = instantiateCount;
            Parent = Entity.Null;
            Transform = transform;
            OnInstantiate = onInstantiate == null ? null : BurstCompiler.CompileFunctionPointer(onInstantiate);
        }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount, Entity parent, LocalTransform transform)
        {
            TableKey = tableKey;
            Key = key;
            InstantiateCount = instantiateCount;
            Parent = parent;
            Transform = transform;
            OnInstantiate = null;
        }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount, Entity parent, LocalTransform transform,
            InstantiateEntityPrefabCallback onInstantiate)
        {
            TableKey = tableKey;
            Key = key;
            InstantiateCount = instantiateCount;
            Parent = parent;
            Transform = transform;
            OnInstantiate = onInstantiate == null ? null : BurstCompiler.CompileFunctionPointer(onInstantiate);
        }

        public InstantiateEntityPrefabCall(string tableName, string keyName, int instantiateCount,
            InstantiateEntityPrefabCallback onInstantiate = null)
            : this(Pop.Hash32(tableName), Pop.Hash32(keyName), instantiateCount, onInstantiate)
        {
        }

        public InstantiateEntityPrefabCall(string tableName, string keyName, int instantiateCount, Entity parent,
            InstantiateEntityPrefabCallback onInstantiate = null)
            : this(Pop.Hash32(tableName), Pop.Hash32(keyName), instantiateCount, parent, onInstantiate)
        {
        }

        public InstantiateEntityPrefabCall(string tableName, string keyName, int instantiateCount, LocalTransform transform,
            InstantiateEntityPrefabCallback onInstantiate = null)
            : this(Pop.Hash32(tableName), Pop.Hash32(keyName), instantiateCount, transform, onInstantiate)
        {
        }

        public InstantiateEntityPrefabCall(string tableName, string keyName, int instantiateCount, Entity parent, LocalTransform transform,
            InstantiateEntityPrefabCallback onInstantiate = null)
            : this(Pop.Hash32(tableName), Pop.Hash32(keyName), instantiateCount, parent, transform, onInstantiate)
        {
        }
    }

    public readonly struct InstantiateEntityPrefabResult : IBufferElementData
    {
        public readonly Entity Entity;
        public readonly int Index;

        public InstantiateEntityPrefabResult(Entity entity, int index)
        {
            Entity = entity;
            Index = index;
        }
    }
}