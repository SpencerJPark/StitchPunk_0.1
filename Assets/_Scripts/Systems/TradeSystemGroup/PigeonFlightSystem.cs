using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Flies the pigeon to the player's arm target, keeps it on the arm while the letter is read, and flies it off afterwards.
// Placeholder motion until a rig exists.
[BurstCompile]
[UpdateInGroup(typeof(TradeSystemGroup))]
[UpdateAfter(typeof(PigeonLetterSystem))]
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
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradePigeonSettings>();
        TradePigeonSettings settings = SystemAPI.GetComponent<TradePigeonSettings>(boardEntity);
        EntityCommandBuffer commandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        bool hasArmPosition = false;
        float3 armPosition = float3.zero;
        foreach (RefRO<LocalToWorld> armTransform in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<PigeonArmTarget>())
        {
            armPosition = armTransform.ValueRO.Position;
            hasArmPosition = true;
            break;
        }

        if (!hasArmPosition && SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
        {
            armPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).TransformPoint(settings.fallbackArmLocalOffset);
            hasArmPosition = true;
        }

        foreach ((RefRW<LocalTransform> transform, RefRW<PigeonFlight> flight, Entity pigeonEntity)
            in SystemAPI.Query<RefRW<LocalTransform>, RefRW<PigeonFlight>>()
                .WithAll<CarrierPigeon>()
                .WithEntityAccess())
        {
            if (flight.ValueRO.phase == PigeonFlightPhase.FlyingOff)
            {
                flight.ValueRW.elapsedSeconds += deltaTime;
                transform.ValueRW.Position += flight.ValueRO.flyOffVelocity * deltaTime;
                if (flight.ValueRO.elapsedSeconds >= settings.flyOffSeconds)
                    commandBuffer.DestroyEntity(pigeonEntity);
                continue;
            }

            if (!hasArmPosition)
                continue;

            flight.ValueRW.elapsedSeconds += deltaTime;
            if (flight.ValueRO.phase == PigeonFlightPhase.FlyingIn)
            {
                float progress = math.saturate(flight.ValueRO.elapsedSeconds / math.max(settings.flyInSeconds, 0.01f));
                float eased = progress * progress * (3f - 2f * progress);
                transform.ValueRW.Position = math.lerp(flight.ValueRO.startPosition, armPosition, eased)
                    + new float3(0f, settings.arcHeight * math.sin(math.PI * progress), 0f);

                if (progress >= 1f)
                {
                    flight.ValueRW.phase = PigeonFlightPhase.Perched;
                    flight.ValueRW.elapsedSeconds = 0f;
                }
            }
            else
            {
                transform.ValueRW.Position = armPosition;
            }
        }
    }
}
