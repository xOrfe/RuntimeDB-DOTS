using Unity.Entities;

namespace XO.RuntimeDatabase
{
    public static class RuntimeDB
    {
        public static void DestroyEntity(EntityManager em, Entity entity)
        {
            em.DestroyEntity(entity);
        }

        public static void DestroyEntity(EntityCommandBuffer ecb, Entity entity)
        {
            ecb.DestroyEntity(entity);
        }
    }
}