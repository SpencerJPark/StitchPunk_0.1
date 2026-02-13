using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

public class SpatialHashSingletonAuthoring : MonoBehaviour
{
    public class Baker : Baker<SpatialHashSingletonAuthoring>
    {
        public override void Bake(SpatialHashSingletonAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
        }
    }
}

public struct SpatialHashSingleton : IComponentData
{
    public NativeParallelMultiHashMap<int2, Entity> npcCells;
    public NativeParallelMultiHashMap<int2, Entity> waypointCells;
}