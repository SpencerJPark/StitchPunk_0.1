using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Flies the pigeon in, keeps it on the (moving) caravan, and flies it off once the letter is read. Placeholder motion until a rig exists.
[BurstCompile]
[UpdateInGroup(typeof(TradeSystemGroup))]
public partial struct PigeonFlightSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TradePigeonSettings>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        TradePigeonSettings settings = SystemAPI.GetSingleton<TradePigeonSettings>();
        EntityCommandBuffer commandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        bool hasCaravan = SystemAPI.TryGetSingletonEntity<Caravan>(out Entity caravanEntity);
        LocalTransform caravanTransform = hasCaravan ? SystemAPI.GetComponent<LocalTransform>(caravanEntity) : default;

        foreach ((RefRW<LocalTransform> transform, RefRW<PigeonFlight> flight, EnabledRefRW<PlayerInteractable> playerInteractableEnabled, Entity pigeonEntity)
            in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PigeonFlight>, EnabledRefRW<PlayerInteractable>>()
                .WithAll<CarrierPigeon>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .WithEntityAccess())
        {
            float3 perchPosition = hasCaravan
                ? caravanTransform.TransformPoint(settings.perchLocalOffset)
                : flight.ValueRO.fallbackPerchPosition;

            if (flight.ValueRO.phase == PigeonFlightPhase.FlyingIn)
            {
                flight.ValueRW.elapsedSeconds += deltaTime;
                float progress = math.saturate(flight.ValueRO.elapsedSeconds / math.max(settings.flyInSeconds, 0.01f));
                float eased = progress * progress * (3f - 2f * progress);
                transform.ValueRW.Position = math.lerp(flight.ValueRO.startPosition, perchPosition, eased)
                    + new float3(0f, settings.arcHeight * math.sin(math.PI * progress), 0f);

                if (progress >= 1f)
                {
                    flight.ValueRW.phase = PigeonFlightPhase.Perched;
                    playerInteractableEnabled.ValueRW = true;
                }
            }
            else if (flight.ValueRO.phase == PigeonFlightPhase.Perched)
            {
                transform.ValueRW.Position = perchPosition;
            }
            else
            {
                flight.ValueRW.elapsedSeconds += deltaTime;
                transform.ValueRW.Position += flight.ValueRO.flyOffVelocity * deltaTime;
                if (flight.ValueRO.elapsedSeconds >= settings.flyOffSeconds)
                    commandBuffer.DestroyEntity(pigeonEntity);
            }
        }
    }
}
