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
        foreach ((EnabledRefRW<OnPlayerReviverEquip> onPlayerReviverEquipEnabled, RefRO<Target> target, EnabledRefRO<Target> targetEnabled) in
            SystemAPI.Query<
                EnabledRefRW<OnPlayerReviverEquip>,
                RefRO<Target>,
                EnabledRefRO<Target>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!onPlayerReviverEquipEnabled.ValueRO) continue;

            if (targetEnabled.ValueRO)
            {
                Entity targetEntity = target.ValueRO.entity;
                if (SystemAPI.HasComponent<ReviveRequest>(targetEntity))
                    SystemAPI.SetComponentEnabled<ReviveRequest>(targetEntity, true);
            }

            onPlayerReviverEquipEnabled.ValueRW = false;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
