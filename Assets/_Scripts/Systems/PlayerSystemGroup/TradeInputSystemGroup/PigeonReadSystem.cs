using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// Interacting with a perched pigeon opens the letter; opening it by any route (the HUD button too) sends the pigeon off.
// Lives in TradeInputSystemGroup because PlayerPickupJob clears the E press later in the frame.
[BurstCompile]
[UpdateInGroup(typeof(TradeInputSystemGroup))]
public partial struct PigeonReadSystem : ISystem
{
    private static readonly float3 FlyOffVelocity = new float3(-9f, 7f, 9f);

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TradeBoard>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradeBoard>();
        bool letterIsOpen = SystemAPI.IsComponentEnabled<TradeLetterOpen>(boardEntity);

        foreach ((EnabledRefRW<OnInteractPlayerInput> onInteractPlayerInputEnabled, RefRO<Target> target, EnabledRefRO<Target> targetEnabled)
            in SystemAPI.Query<EnabledRefRW<OnInteractPlayerInput>, RefRO<Target>, EnabledRefRO<Target>>()
                .WithAll<Player>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!onInteractPlayerInputEnabled.ValueRO || !targetEnabled.ValueRO)
                continue;

            Entity targetEntity = target.ValueRO.entity;
            if (!SystemAPI.HasComponent<PigeonFlight>(targetEntity)
                || SystemAPI.GetComponent<PigeonFlight>(targetEntity).phase != PigeonFlightPhase.Perched)
                continue;

            onInteractPlayerInputEnabled.ValueRW = false;
            letterIsOpen = true;
            SystemAPI.SetComponentEnabled<TradeLetterOpen>(boardEntity, true);
        }

        if (!letterIsOpen)
            return;

        foreach ((RefRW<PigeonFlight> flight, EnabledRefRW<PlayerInteractable> playerInteractableEnabled)
            in SystemAPI.Query<RefRW<PigeonFlight>, EnabledRefRW<PlayerInteractable>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (flight.ValueRO.phase != PigeonFlightPhase.Perched)
                continue;

            flight.ValueRW.phase = PigeonFlightPhase.FlyingOff;
            flight.ValueRW.elapsedSeconds = 0f;
            flight.ValueRW.flyOffVelocity = FlyOffVelocity;
            playerInteractableEnabled.ValueRW = false;
        }
    }
}
