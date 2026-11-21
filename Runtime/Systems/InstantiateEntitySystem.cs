using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;
using XO.PopUtils;

namespace XO.RuntimeDatabase
{
    public partial struct InstantiateEntitySystem : ISystem
    {
        public NativeHashMap<ulong, Entity> PrefabCache;
        public NativeHashSet<ulong> CurrentlyLoading;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            PrefabCache = new NativeHashMap<ulong, Entity>(16, Allocator.Persistent);
            CurrentlyLoading = new NativeHashSet<ulong>(16, Allocator.Persistent);
            state.RequireForUpdate<InstantiateEntityPrefabCall>();
        }

        private struct InstantiateCallData
        {
            public Entity CallEntity;
            public Entity PrefabRoot;
            public InstantiateEntityPrefabCall Call;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!PrefabCache.IsCreated)
            {
                PrefabCache = new NativeHashMap<ulong, Entity>(16, Allocator.Persistent);
            }
            if (!CurrentlyLoading.IsCreated)
            {
                CurrentlyLoading = new NativeHashSet<ulong>(16, Allocator.Persistent);
            }

            bool hasDatabase = SystemAPI.TryGetSingleton<RuntimeTable<EntityPrefabReference>>(out var database);
            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var readyCalls = new NativeList<InstantiateCallData>(Allocator.Temp);

            // 0. Process auto-baked prefabs from buffer into cache
            foreach (var (buffer, bufferEntity) in
                     SystemAPI.Query<DynamicBuffer<LoadedEntityPrefabBuffer>>()
                         .WithEntityAccess())
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    var entry = buffer[i];
                    var cacheKey = ((ulong)entry.TableKey << 32) | entry.Key;
                    PrefabCache.TryAdd(cacheKey, entry.Prefab);
                }
                ecb.RemoveComponent<LoadedEntityPrefabBuffer>(bufferEntity);
            }

            DynamicBuffer<PrefabCatalogElement> catalog = default;
            bool hasCatalog = SystemAPI.TryGetSingletonBuffer<PrefabCatalogElement>(out catalog);

            // 1. Process instantiation calls
            foreach (var (call, entity) in
                     SystemAPI.Query<RefRO<InstantiateEntityPrefabCall>>()
                         .WithNone<RequestEntityPrefabLoaded>()
                         .WithEntityAccess())
            {
                var cacheKey = ((ulong)call.ValueRO.TableKey << 32) | call.ValueRO.Key;
                var isCached = PrefabCache.TryGetValue(cacheKey, out var prefab);
                if (!isCached && hasCatalog)
                {
                    isCached = catalog.TryGetPrefab(call.ValueRO.TableKey, call.ValueRO.Key, out prefab);
                    if (!isCached)
                    {
                        isCached = catalog.TryGetPrefab(call.ValueRO.Key, out prefab);
                    }
                    if (isCached)
                    {
                        PrefabCache.TryAdd(cacheKey, prefab);
                    }
                }

                if (isCached)
                {
                    readyCalls.Add(new InstantiateCallData
                    {
                        CallEntity = entity,
                        PrefabRoot = prefab,
                        Call = call.ValueRO
                    });
                }
                else
                {
                    // If this key is already being loaded asynchronously, skip starting a new loading stream
                    if (CurrentlyLoading.Contains(cacheKey))
                    {
                        continue;
                    }

                    EntityPrefabReference entityPrefab = default;
                    var prefabValid = hasDatabase && database.TryGetValue(call.ValueRO.TableKey, call.ValueRO.Key, out entityPrefab);
                    if (!prefabValid)
                    {
                        Debug.LogError($"Prefab with key {call.ValueRO.Key} under table {call.ValueRO.TableKey} not found in database or catalog");
                        continue;
                    }

                    CurrentlyLoading.Add(cacheKey);
                    var request = new RequestEntityPrefabLoaded { Prefab = entityPrefab };
                    ecb.AddComponent(entity, request);
                }
            }

            // 2. Process finished loading results
            foreach (var (result, call, entity) in
                     SystemAPI.Query<RefRO<PrefabLoadResult>, RefRO<InstantiateEntityPrefabCall>>()
                         .WithEntityAccess())
            {
                var cacheKey = ((ulong)call.ValueRO.TableKey << 32) | call.ValueRO.Key;
                // Cache the loaded prefab root so all subsequent calls can reuse it instantly
                PrefabCache.TryAdd(cacheKey, result.ValueRO.PrefabRoot);
                CurrentlyLoading.Remove(cacheKey);

                readyCalls.Add(new InstantiateCallData
                {
                    CallEntity = entity,
                    PrefabRoot = result.ValueRO.PrefabRoot,
                    Call = call.ValueRO
                });

                ecb.RemoveComponent<RequestEntityPrefabLoaded>(entity);
                ecb.RemoveComponent<PrefabLoadResult>(entity);
            }

            ecb.Playback(em);
            ecb.Dispose();

            // 3. Process instantiations directly via EntityManager so buffer contains real entity references
            if (readyCalls.Length > 0)
            {
                var sharedCallbackEcb = new EntityCommandBuffer(Allocator.Temp);
                for (var i = 0; i < readyCalls.Length; i++)
                {
                    var item = readyCalls[i];
                    HandleInstantiateCall(em, sharedCallbackEcb, item.CallEntity, item.PrefabRoot, item.Call);
                }
                sharedCallbackEcb.Playback(em);
                sharedCallbackEcb.Dispose();
            }
            readyCalls.Dispose();
        }

        private void HandleInstantiateCall(EntityManager em, EntityCommandBuffer sharedCallbackEcb, Entity callEntity,
            Entity prefabRoot,
            InstantiateEntityPrefabCall call)
        {
            if (!em.Exists(callEntity) || !em.Exists(prefabRoot)) return;

            var entities = new NativeArray<Entity>(call.InstantiateCount, Allocator.Temp);

            for (var i = 0; i < call.InstantiateCount; i++)
            {
                var entity = em.Instantiate(prefabRoot);
                var parent = call.Parent;
                if (em.Exists(parent))
                {
                    em.SetParent(entity, parent);
                }

                em.SetComponentData(entity, call.Transform);
                entities[i] = entity;

                if (call.OnInstantiate != null)
                {
                    call.OnInstantiate.Value.Invoke(sharedCallbackEcb, entity, i);
                }
            }

            var resultBuffer = em.HasBuffer<InstantiateEntityPrefabResult>(callEntity)
                ? em.GetBuffer<InstantiateEntityPrefabResult>(callEntity)
                : em.AddBuffer<InstantiateEntityPrefabResult>(callEntity);
            resultBuffer.Clear();
            for (var i = 0; i < entities.Length; i++)
            {
                resultBuffer.Add(new InstantiateEntityPrefabResult(entities[i], i));
            }
            entities.Dispose();

            em.RemoveComponent<InstantiateEntityPrefabCall>(callEntity);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (PrefabCache.IsCreated)
            {
                PrefabCache.Dispose();
            }
            if (CurrentlyLoading.IsCreated)
            {
                CurrentlyLoading.Dispose();
            }
        }
    }
}