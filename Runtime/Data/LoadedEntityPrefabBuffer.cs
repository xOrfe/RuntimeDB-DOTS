using Unity.Entities;

namespace XO.RuntimeDatabase
{
    public struct LoadedEntityPrefabBuffer : IBufferElementData
    {
        public uint TableKey;
        public uint Key;
        public Entity Prefab;

        public ulong CacheKey => ((ulong)TableKey << 32) | Key;
    }
}