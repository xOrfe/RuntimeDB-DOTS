using UnityEngine;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using XO.Curve;
using XO.PopUtils;
using XO.RuntimeDatabase;

namespace XO.RuntimeDatabase.Authoring
{
    [CreateAssetMenu(fileName = "GameobjectTable", menuName = "XO/RuntimeDatabase/Tables/GameobjectTable", order = 1)]
    public class PrefabTableSo : RuntimeTableSo<GameObject, EntityPrefabReference>, IRuntimePrefabTable
    {
        public override EntityPrefabReference Convert(GameObject source, Baker<RuntimeDatabaseAuthoring> baker)
        {
#if UNITY_EDITOR
            return new EntityPrefabReference(source);
#else
            throw new System.InvalidOperationException("Prefab conversion is an editor baking operation.");
#endif
        }

        public void CollectPrefabs(Baker<RuntimeDatabaseAuthoring> baker, System.Collections.Generic.List<PrefabEntryMetadata> outPrefabs)
        {
            if (data == null || data.Length == 0) return;

            for (int i = 0; i < data.Length; i++)
            {
                var entry = data[i];
                if (entry.value != null)
                {
                    baker.DependsOn(entry.value);
                    var entityPrefab = baker.GetEntity(entry.value, TransformUsageFlags.Dynamic);
                    outPrefabs.Add(new PrefabEntryMetadata
                    {
                        PrefabKey = Pop.Hash32(entry.key),
                        PrefabEntity = entityPrefab
                    });
                }
            }
        }
    }

    [CreateAssetMenu(fileName = "Float3SplineTable", menuName = "XO/RuntimeDatabase/Tables/Float3SplineTable", order = 2)]
    public class Float3SplineTableSo : RuntimeTableSo<SerializableSpline<float3>, BlobAssetReference<SplineBlob<float3>>>
    {
        public override BlobAssetReference<SplineBlob<float3>> Convert(SerializableSpline<float3> source, Baker<RuntimeDatabaseAuthoring> baker)
        {
            var spline = Spline.CreateSplineBlob(source, new Float3Math());
            baker.AddBlobAsset(ref spline, out var hash);
            return spline;
        }
    }

    [CreateAssetMenu(fileName = "QuaternionSplineTable", menuName = "XO/RuntimeDatabase/Tables/QuaternionSplineTable", order = 3)]
    public class QuaternionSplineDataTableSo : RuntimeTableSo<SerializableSpline<quaternion>, BlobAssetReference<SplineBlob<quaternion>>>
    {
        public override BlobAssetReference<SplineBlob<quaternion>> Convert(SerializableSpline<quaternion> source, Baker<RuntimeDatabaseAuthoring> baker)
        {
            var spline = Spline.CreateSplineBlob(source, new QuaternionMath());
            baker.AddBlobAsset(ref spline, out var hash);
            return spline;
        }
    }
}
