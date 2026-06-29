using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Bake-time bridge for DesignAuthoring.reloadDesign. Pre-placed (subscene-baked) units never pass
// through UnitSpawnerSystem, so NewlySpawned is never enabled and the spawn-init design pipeline
// (AnimatorTargetInitSystem → DesignRandomizeSystem → DesignApplySystem) never runs on them — they
// also never build their AnimatorTarget buffer. This system enables NewlySpawned on every flagged
// unit after all bakers complete, so the pipeline runs once on load and the unit gets a random design.
// Prefabs are excluded (default query skips the Prefab tag): runtime spawns get NewlySpawned from
// the spawner already. The DesignReloadOnBake marker is [BakingType] and stripped from the runtime world.
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct DesignReloadBakingSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityQuery flaggedUnits = SystemAPI.QueryBuilder().WithAll<DesignReloadOnBake>().Build();
        NativeArray<Entity> entities = flaggedUnits.ToEntityArray(Allocator.Temp);

        for (int index = 0; index < entities.Length; index++)
        {
            Entity unitEntity = entities[index];
            if (state.EntityManager.HasComponent<NewlySpawned>(unitEntity))
                state.EntityManager.SetComponentEnabled<NewlySpawned>(unitEntity, true);
        }

        entities.Dispose();
    }
}
