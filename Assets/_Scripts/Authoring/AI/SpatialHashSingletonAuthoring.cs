using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

public class SpatialHashSingletonAuthoring : MonoBehaviour
{
    public int initialCapacity = 1000;

    public class Baker : Baker<SpatialHashSingletonAuthoring>
    {
        public override void Bake(SpatialHashSingletonAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            // Component added at runtime since it needs NativeContainers
        }
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SpatialHashInitSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        Entity entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new SpatialHashSingleton
        {
            npcCells = new NativeParallelMultiHashMap<int2, Entity>(1000, Allocator.Persistent),
            waypointCells = new NativeParallelMultiHashMap<int2, Entity>(500, Allocator.Persistent)
        });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<SpatialHashSingleton>(out var singleton))
        {
            singleton.ValueRW.npcCells.Dispose();
            singleton.ValueRW.waypointCells.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;
    }
}