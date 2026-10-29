using Unity.Burst;
using Unity.Entities;

// E at a drop-off becomes a TradeHandOverRequest on it (TR-A5: handing units over is always the player's explicit act).
// Lives here, not in TradeSystemGroup, because PlayerPickupJob clears the E press inside Item (Gotchas.md).
[BurstCompile]
[UpdateInGroup(typeof(TradeInputSystemGroup))]
public partial struct DropOffInteractSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach ((EnabledRefRW<OnInteractPlayerInput> onInteractPlayerInputEnabled, RefRO<Target> target, EnabledRefRO<Target> targetEnabled)
            in SystemAPI.Query<EnabledRefRW<OnInteractPlayerInput>, RefRO<Target>, EnabledRefRO<Target>>()
                .WithAll<Player>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!onInteractPlayerInputEnabled.ValueRO || !targetEnabled.ValueRO)
                continue;

            Entity targetEntity = target.ValueRO.entity;
            if (!SystemAPI.HasComponent<TradeDropOff>(targetEntity))
                continue;

            onInteractPlayerInputEnabled.ValueRW = false;
            SystemAPI.SetComponentEnabled<TradeHandOverRequest>(targetEntity, true);
        }
    }
}
