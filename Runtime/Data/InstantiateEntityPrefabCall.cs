using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

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
        public readonly FunctionPointer<InstantiateEntityPrefabCallback> OnInstantiate;

        public InstantiateEntityPrefabCall(
            uint tableKey,
            uint key,
            int instantiateCount,
            Entity parent,
            LocalTransform transform,
            FunctionPointer<InstantiateEntityPrefabCallback> onInstantiate = default)
        {
            TableKey = tableKey;
            Key = key;
            InstantiateCount = instantiateCount;
            Parent = parent;
            Transform = transform;
            OnInstantiate = onInstantiate;
        }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount = 1)
            : this(tableKey, key, instantiateCount, Entity.Null, LocalTransform.Identity)
        {
        }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount, Entity parent)
            : this(tableKey, key, instantiateCount, parent, LocalTransform.Identity)
        {
        }

        public InstantiateEntityPrefabCall(uint tableKey, uint key, int instantiateCount, LocalTransform transform)
            : this(tableKey, key, instantiateCount, Entity.Null, transform)
        {
        }

        public InstantiateEntityPrefabCall(
            string tableName,
            string keyName,
            int instantiateCount,
            Entity parent,
            LocalTransform transform,
            FunctionPointer<InstantiateEntityPrefabCallback> onInstantiate = default)
            : this(RuntimeDB.Hash32(tableName), RuntimeDB.Hash32(keyName), instantiateCount, parent, transform, onInstantiate)
        {
        }

        public InstantiateEntityPrefabCall(string tableName, string keyName, int instantiateCount = 1)
            : this(RuntimeDB.Hash32(tableName), RuntimeDB.Hash32(keyName), instantiateCount, Entity.Null, LocalTransform.Identity)
        {
        }

        public InstantiateEntityPrefabCall(string tableName, string keyName, int instantiateCount, Entity parent)
            : this(RuntimeDB.Hash32(tableName), RuntimeDB.Hash32(keyName), instantiateCount, parent, LocalTransform.Identity)
        {
        }

        public InstantiateEntityPrefabCall(string tableName, string keyName, int instantiateCount, LocalTransform transform)
            : this(RuntimeDB.Hash32(tableName), RuntimeDB.Hash32(keyName), instantiateCount, Entity.Null, transform)
        {
        }

        /// <summary>
        /// Managed helper that compiles a Burst function pointer for the post-instantiation callback.
        /// Call from managed code (not inside a Burst-compiled job).
        /// </summary>
        public InstantiateEntityPrefabCall WithCallback(InstantiateEntityPrefabCallback onInstantiate)
        {
            var fp = onInstantiate != null
                ? BurstCompiler.CompileFunctionPointer(onInstantiate)
                : default;
            return new InstantiateEntityPrefabCall(TableKey, Key, InstantiateCount, Parent, Transform, fp);
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
