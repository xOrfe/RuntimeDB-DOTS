using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;
using UnityEngine;

namespace XO.RuntimeDatabase
{
    public partial struct InstantiateEntitySystem : ISystem
    {
        public NativeHashMap<ulong, Entity> PrefabCache;
        public NativeHashMap<ulong, Entity> CurrentlyLoading;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            PrefabCache = new NativeHashMap<ulong, Entity>(16, Allocator.Persistent);
            CurrentlyLoading = new NativeHashMap<ulong, Entity>(16, Allocator.Persistent);
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
                PrefabCache = new NativeHashMap<ulong, Entity>(16, Allocator.Persistent);
            if (!CurrentlyLoading.IsCreated)
                CurrentlyLoading = new NativeHashMap<ulong, Entity>(16, Allocator.Persistent);

            var em = state.EntityManager;
            CleanupStaleLoadingRequests(em);

            bool hasDatabase = SystemAPI.TryGetSingleton<RuntimeTable<EntityPrefabReference>>(out var database);
            bool hasCatalog = SystemAPI.TryGetSingletonBuffer<PrefabCatalogElement>(out var catalog);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var readyCalls = new NativeList<InstantiateCallData>(Allocator.Temp);

            // 1. Process new instantiation calls
            foreach (var (call, entity) in
                     SystemAPI.Query<RefRO<InstantiateEntityPrefabCall>>()
                         .WithNone<RequestEntityPrefabLoaded>()
                         .WithEntityAccess())
            {
                if (call.ValueRO.InstantiateCount <= 0)
                {
                    ecb.RemoveComponent<InstantiateEntityPrefabCall>(entity);
                    continue;
                }

                var cacheKey = RuntimeDB.PackCacheKey(call.ValueRO.TableKey, call.ValueRO.Key);
                bool isCached = PrefabCache.TryGetValue(cacheKey, out var prefab) && em.Exists(prefab);

                if (!isCached && hasCatalog)
                {
                    isCached = catalog.TryGetPrefab(call.ValueRO.TableKey, call.ValueRO.Key, out prefab);
                    if (isCached && em.Exists(prefab))
                    {
                        PrefabCache[cacheKey] = prefab;
                    }
                    else
                    {
                        isCached = false;
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
                    continue;
                }

                // Skip starting a duplicate async load if already in flight
                if (CurrentlyLoading.ContainsKey(cacheKey))
                {
                    continue;
                }

                if (!hasDatabase || !database.TryGetValue(call.ValueRO.TableKey, call.ValueRO.Key, out var entityPrefab))
                {
                    var msg = new FixedString128Bytes();
                    msg.Append("[RuntimeDB] Prefab not found. TableKey=");
                    msg.Append(call.ValueRO.TableKey);
                    msg.Append(", Key=");
                    msg.Append(call.ValueRO.Key);
                    Debug.LogError(msg);
                    ecb.RemoveComponent<InstantiateEntityPrefabCall>(entity);
                    continue;
                }

                CurrentlyLoading.TryAdd(cacheKey, entity);
                ecb.AddComponent(entity, new RequestEntityPrefabLoaded { Prefab = entityPrefab });
            }

            // 2. Process finished async prefab loads
            foreach (var (result, call, entity) in
                     SystemAPI.Query<RefRO<PrefabLoadResult>, RefRO<InstantiateEntityPrefabCall>>()
                         .WithEntityAccess())
            {
                var cacheKey = RuntimeDB.PackCacheKey(call.ValueRO.TableKey, call.ValueRO.Key);
                PrefabCache[cacheKey] = result.ValueRO.PrefabRoot;
                CurrentlyLoading.Remove(cacheKey);

                if (call.ValueRO.InstantiateCount > 0)
                {
                    readyCalls.Add(new InstantiateCallData
                    {
                        CallEntity = entity,
                        PrefabRoot = result.ValueRO.PrefabRoot,
                        Call = call.ValueRO
                    });
                }
                else
                {
                    ecb.RemoveComponent<InstantiateEntityPrefabCall>(entity);
                }

                ecb.RemoveComponent<RequestEntityPrefabLoaded>(entity);
                ecb.RemoveComponent<PrefabLoadResult>(entity);
            }

            ecb.Playback(em);
            ecb.Dispose();

            // 3. Instantiate directly via EntityManager so result buffer contains real Entity references
            if (readyCalls.Length > 0)
            {
                var sharedCallbackEcb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < readyCalls.Length; i++)
                {
                    var item = readyCalls[i];
                    HandleInstantiateCall(em, sharedCallbackEcb, item.CallEntity, item.PrefabRoot, item.Call);
                }
                sharedCallbackEcb.Playback(em);
                sharedCallbackEcb.Dispose();
            }

            readyCalls.Dispose();
        }

        private void CleanupStaleLoadingRequests(EntityManager em)
        {
            if (CurrentlyLoading.Count == 0) return;

            var staleKeys = new NativeList<ulong>(Allocator.Temp);
            foreach (var kvp in CurrentlyLoading)
            {
                var loadingEntity = kvp.Value;
                if (!em.Exists(loadingEntity) || !em.HasComponent<RequestEntityPrefabLoaded>(loadingEntity))
                {
                    staleKeys.Add(kvp.Key);
                }
            }

            for (int i = 0; i < staleKeys.Length; i++)
            {
                CurrentlyLoading.Remove(staleKeys[i]);
            }

            staleKeys.Dispose();
        }

        private static void HandleInstantiateCall(
            EntityManager em,
            EntityCommandBuffer sharedCallbackEcb,
            Entity callEntity,
            Entity prefabRoot,
            InstantiateEntityPrefabCall call)
        {
            if (!em.Exists(callEntity) || !em.Exists(prefabRoot) || call.InstantiateCount <= 0)
                return;

            var entities = new NativeArray<Entity>(call.InstantiateCount, Allocator.Temp);

            for (int i = 0; i < call.InstantiateCount; i++)
            {
                var entity = em.Instantiate(prefabRoot);
                if (em.Exists(call.Parent))
                {
                    RuntimeDB.SetParent(em, entity, call.Parent);
                }

                em.SetComponentData(entity, call.Transform);
                entities[i] = entity;

                if (call.OnInstantiate.IsCreated)
                {
                    call.OnInstantiate.Invoke(sharedCallbackEcb, entity, i);
                }
            }

            var resultBuffer = em.HasBuffer<InstantiateEntityPrefabResult>(callEntity)
                ? em.GetBuffer<InstantiateEntityPrefabResult>(callEntity)
                : em.AddBuffer<InstantiateEntityPrefabResult>(callEntity);
            resultBuffer.Clear();
            for (int i = 0; i < entities.Length; i++)
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
                PrefabCache.Dispose();
            if (CurrentlyLoading.IsCreated)
                CurrentlyLoading.Dispose();
        }
    }
}
