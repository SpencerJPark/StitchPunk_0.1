using Unity.Entities;

[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateBefore(typeof(AutoSaveTimerSystem))]
public partial struct AutoSaveCombatQuietSystem : ISystem
{
    private EntityQuery unitsInCombatQuery;
    private EntityQuery autoSavePendingQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameDataTag>();

        unitsInCombatQuery = SystemAPI.QueryBuilder()
            .WithAll<AggressiveState>()
            .WithDisabled<Dead>()
            .Build();
        autoSavePendingQuery = SystemAPI.QueryBuilder().WithAll<AutoSavePending>().Build();

        Entity quietEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(quietEntity, new AutoSaveCombatQuiet
        {
            secondsSinceCombat = 0f,
            requiredQuietSeconds = 3f,
        });
    }

    public void OnUpdate(ref SystemState state)
    {
        RefRW<AutoSaveCombatQuiet> combatQuiet = SystemAPI.GetSingletonRW<AutoSaveCombatQuiet>();
        if (unitsInCombatQuery.IsEmptyIgnoreFilter)
            combatQuiet.ValueRW.secondsSinceCombat += SystemAPI.Time.DeltaTime;
        else
            combatQuiet.ValueRW.secondsSinceCombat = 0f;

        if (autoSavePendingQuery.IsEmptyIgnoreFilter)
            return;
        if (combatQuiet.ValueRO.secondsSinceCombat < combatQuiet.ValueRO.requiredQuietSeconds)
            return;

        Entity saveEntity = SystemAPI.GetSingletonEntity<GameDataTag>();
        if (SystemAPI.IsComponentEnabled<SaveRequest>(saveEntity))
            return;

        SystemAPI.SetComponent(saveEntity, new SaveRequest { kind = (int)SaveKind.Autosave });
        SystemAPI.SetComponentEnabled<SaveRequest>(saveEntity, true);
        state.EntityManager.DestroyEntity(autoSavePendingQuery);
    }
}
