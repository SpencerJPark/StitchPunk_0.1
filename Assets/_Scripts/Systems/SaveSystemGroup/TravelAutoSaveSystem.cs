using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateBefore(typeof(AutoSaveCombatQuietSystem))]
public partial struct TravelAutoSaveSystem : ISystem
{
    private EntityQuery autoSavePendingQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameDataTag>();

        autoSavePendingQuery = SystemAPI.QueryBuilder().WithAll<AutoSavePending>().Build();

        Entity travelEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(travelEntity, new TravelAutoSaveState { thresholdMetres = 50f });
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        float3 playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
        RefRW<TravelAutoSaveState> travelState = SystemAPI.GetSingletonRW<TravelAutoSaveState>();

        if (!travelState.ValueRO.hasLastSavedPosition)
        {
            travelState.ValueRW.lastSavedPlayerPosition = playerPosition;
            travelState.ValueRW.hasLastSavedPosition = true;
            return;
        }

        if (math.distance(playerPosition, travelState.ValueRO.lastSavedPlayerPosition) < travelState.ValueRO.thresholdMetres)
            return;

        if (autoSavePendingQuery.IsEmptyIgnoreFilter)
            state.EntityManager.CreateEntity(typeof(AutoSavePending));
        travelState.ValueRW.lastSavedPlayerPosition = playerPosition;
    }
}
