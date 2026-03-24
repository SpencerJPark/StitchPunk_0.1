using Unity.Burst;
using Unity.Entities;

[UpdateInGroup(typeof(GameManagerSystemGroup), OrderFirst = true)]
public partial struct PlayerInputSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
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
