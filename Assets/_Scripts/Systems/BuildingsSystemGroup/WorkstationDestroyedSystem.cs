using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct WorkstationDestroyedSystem : ISystem
{
    // Ignores the enabled bit: CampLoopGameOver is baked disabled, so a normal query would never find it.
    private EntityQuery gameOverSingletonQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        gameOverSingletonQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithPresent<CampLoopGameOver>()
            .Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // The *Singleton* query API throws on enableable component types, so read the entity array instead.
        NativeArray<Entity> gameOverEntities = gameOverSingletonQuery.ToEntityArray(Allocator.Temp);
        if (gameOverEntities.Length == 0)
        {
            return;
        }
        Entity gameOverEntity = gameOverEntities[0];

        foreach ((RefRO<Health> health, RefRO<Workstation> workstation) in SystemAPI.Query<RefRO<Health>, RefRO<Workstation>>())
        {
            if (workstation.ValueRO.ownerPlayer == Entity.Null || health.ValueRO.healthAmount > 0)
            {
                continue;
            }

            SystemAPI.SetComponentEnabled<CampLoopGameOver>(gameOverEntity, true);
            return;
        }
    }
}
