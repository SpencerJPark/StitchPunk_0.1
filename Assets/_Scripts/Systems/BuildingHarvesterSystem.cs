using Unity.Burst;
using Unity.Entities;

partial struct BuildingHarvesterSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        foreach (RefRW<BuildingHarvester> buildingHarvester in SystemAPI.Query<RefRW<BuildingHarvester>>())
        {
            buildingHarvester.ValueRW.harvestTimer -= SystemAPI.Time.DeltaTime;
            if (buildingHarvester.ValueRW.harvestTimer <= 0)
            {
                buildingHarvester.ValueRW.harvestTimer = buildingHarvester.ValueRO.harvestTimerMax;
                    
                ResourceManager.Instance.AddResourceAmount(buildingHarvester.ValueRO.resourceType, 1);
            }
        }
    }
}
