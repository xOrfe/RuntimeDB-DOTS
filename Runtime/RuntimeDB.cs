using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace XO.RuntimeDatabase
{
    public static class RuntimeDB
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash32(string value)
        {
            if (value == null) return FnvOffsetBasis;

            unchecked
            {
                uint hash = FnvOffsetBasis;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash32<T>(in T value) where T : unmanaged, INativeList<byte>
        {
            unchecked
            {
                uint hash = FnvOffsetBasis;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash32(in NativeArray<byte> bytes)
        {
            unchecked
            {
                uint hash = FnvOffsetBasis;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= FnvPrime;
                }
                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong PackCacheKey(uint tableKey, uint objectKey)
        {
            return ((ulong)tableKey << 32) | objectKey;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnpackCacheKey(ulong cacheKey, out uint tableKey, out uint objectKey)
        {
            tableKey = (uint)(cacheKey >> 32);
            objectKey = (uint)(cacheKey & 0xFFFFFFFFu);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetParent(EntityManager em, Entity entity, Entity parent)
        {
            if (em.HasComponent<Parent>(entity))
            {
                em.SetComponentData(entity, new Parent { Value = parent });
            }
            else
            {
                em.AddComponentData(entity, new Parent { Value = parent });
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(EntityManager em, Entity entity)
        {
            em.DestroyEntity(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(EntityCommandBuffer ecb, Entity entity)
        {
            ecb.DestroyEntity(entity);
        }
    }
}
