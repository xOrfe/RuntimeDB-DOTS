# RuntimeDB DOTS

High-performance, unmanaged, Burst-compatible runtime database and prefab instantiation system for Unity DOTS.

**Package:** `com.xorfe.runtimedb-dots` · **Version:** `0.1.0` · **Unity:** `2022.3+` · **License:** MIT

---

## Installation

In Unity Package Manager, select **Add package from git URL**:

```text
https://github.com/xOrfe/RuntimeDB-DOTS.git
```

### Additional Dependencies

- [PopUtils](https://github.com/xOrfe/PopUtils) (`com.xorfe.poputils`)
- [Entityween](https://github.com/xOrfe/Entityween) (`com.xorfe.entityween`, for spline tables)
- Odin Inspector (`SerializedScriptableObject` for authoring tables)

---

## Features

- **`RuntimeTable<TValue>`**: Binary-search unmanaged `BlobAssetReference` tables with $O(\log N)$ Burst-compatible lookups.
- **`PrefabCatalogElement`**: Zero-latency synchronous ECS prefab lookup and instantiation.
- **`InstantiateEntitySystem`**: Asynchronous `EntityPrefabReference` loading, caching, and instantiation via `InstantiateEntityPrefabCall`.
- **Authoring & Diagnostics**: `RuntimeDatabaseAuthoring` baker, `RuntimeTableSo<TSource, TDots>` ScriptableObjects, and the **Tools > Runtime Database Viewer** editor window.
