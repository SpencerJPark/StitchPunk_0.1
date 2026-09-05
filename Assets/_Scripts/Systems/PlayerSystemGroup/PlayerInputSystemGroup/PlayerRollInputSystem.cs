using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(PlayerInputSystemGroup))]
public partial struct PlayerRollInputSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // A scene without the narrative singleton behaves as "no cutscene".
        if (SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity)
            && SystemAPI.IsComponentEnabled<CutsceneActiveTag>(narrativeEntity))
            return;

        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (rollInput, rollEnabled) in
            SystemAPI.Query<
                RefRW<OnRollPlayerInput>,
                EnabledRefRW<OnRollPlayerInput>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!rollEnabled.ValueRO) continue;

            rollInput.ValueRW.rollTime -= deltaTime;
            if (rollInput.ValueRO.rollTime <= 0f)
            {
                rollInput.ValueRW.rollTime = 0f;
                rollEnabled.ValueRW = false;
            }
        }
    }
}
