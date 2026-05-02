using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(PlayerEquipmentSystemGroup))]
public partial struct PlayerReviverSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (reviverEnabled, target, targetEnabled) in
            SystemAPI.Query<
                EnabledRefRW<OnPlayerReviverEquipt>,
                RefRO<Target>,
                EnabledRefRO<Target>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!reviverEnabled.ValueRO) continue;

            if (targetEnabled.ValueRO)
            {
                Entity targetEntity = target.ValueRO.entity;
                if (SystemAPI.HasComponent<ReviveRequest>(targetEntity))
                    SystemAPI.SetComponentEnabled<ReviveRequest>(targetEntity, true);
            }

            reviverEnabled.ValueRW = false;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
