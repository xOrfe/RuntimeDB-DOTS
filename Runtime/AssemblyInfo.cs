using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using XO.Curve;
using XO.RuntimeDatabase;

[assembly: RegisterGenericComponentType(typeof(RuntimeTable<EntityPrefabReference>))]
[assembly: RegisterGenericComponentType(typeof(RuntimeTable<BlobAssetReference<SplineBlob<float3>>>))]
[assembly: RegisterGenericComponentType(typeof(RuntimeTable<BlobAssetReference<SplineBlob<quaternion>>>))]
