using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Plays a finished summon out of the caravan (Caravan_System.md §4). WorkstationSummonSystem starts it; this
// decides when the unit appears. Walk-out: doors open, the unit appears inside and CaravanExitWalkSystem walks
// it down. Smoke burst: doors shut, the caravan shakes, the doors slam open in a cloud, and the unit appears
// once the smoke hides the spot. The next summon waits until the sequence ends (CV-Q3).
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateAfter(typeof(WorkstationSummonSystem))]
public partial struct CaravanExitSequenceSystem : ISystem
{
    // Beat after a walk-out unit lands before the next summon may start.
    private const float WALK_OUT_SETTLE_SECONDS = 0.2f;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        if (SystemAPI.QueryBuilder().WithAll<CaravanExitSequence>().Build().IsEmpty) return;
        // Main-thread lookup reads/writes on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        foreach ((RefRW<CaravanExitSequence> exitSequence, EnabledRefRW<CaravanExitSequence> caravanExitSequenceEnabled,
                     RefRW<CaravanDoors> caravanDoors, RefRW<CaravanShake> caravanShake,
                     RefRW<CaravanPresentationEvents> presentationEvents, RefRO<CaravanExitTuning> exitTuning,
                     DynamicBuffer<PendingMinionSpawn> pendingSpawns, Entity caravanEntity)
                 in SystemAPI.Query<RefRW<CaravanExitSequence>, EnabledRefRW<CaravanExitSequence>, RefRW<CaravanDoors>,
                     RefRW<CaravanShake>, RefRW<CaravanPresentationEvents>, RefRO<CaravanExitTuning>,
                     DynamicBuffer<PendingMinionSpawn>>()
                     .WithEntityAccess())
        {
            CaravanExitTuning tuning = exitTuning.ValueRO;
            LocalTransform caravanPose = SystemAPI.GetComponent<LocalTransform>(caravanEntity);
            Caravan caravan = SystemAPI.GetComponent<Caravan>(caravanEntity);

            if (exitSequence.ValueRO.style == CaravanExitStyle.SmokeBurst)
            {
                // The burst needs shut doors to burst through; hold the clock until they are.
                if (exitSequence.ValueRO.elapsedSeconds <= 0f && caravanDoors.ValueRO.phase != CaravanDoorPhase.Closed)
                {
                    CaravanDoorControl.RequestClose(ref caravanDoors.ValueRW);
                    continue;
                }

                float previousSeconds = exitSequence.ValueRO.elapsedSeconds;
                float elapsedSeconds = previousSeconds + deltaTime;
                exitSequence.ValueRW.elapsedSeconds = elapsedSeconds;

                float burstAtSeconds = tuning.shakeBeforeBurstSeconds;
                float revealAtSeconds = burstAtSeconds + tuning.smokeCoverDelaySeconds;

                if (previousSeconds <= 0f)
                    caravanShake.ValueRW = new CaravanShake { remainingSeconds = tuning.shakeBeforeBurstSeconds, amplitude = tuning.shakeBeforeBurstAmplitude };

                if (Crossed(previousSeconds, elapsedSeconds, burstAtSeconds))
                {
                    CaravanDoorControl.BurstOpen(ref caravanDoors.ValueRW);
                    caravanShake.ValueRW = new CaravanShake { remainingSeconds = tuning.burstShakeSeconds, amplitude = tuning.burstShakeAmplitude };
                    presentationEvents.ValueRW.smokeBurstCount++;
                }

                if (!exitSequence.ValueRO.unitReleased && elapsedSeconds >= revealAtSeconds)
                {
                    pendingSpawns.Add(new PendingMinionSpawn
                    {
                        unitType = exitSequence.ValueRO.unitType,
                        position = caravanPose.TransformPoint(caravan.smokeBurstSpawnLocalOffset),
                    });
                    exitSequence.ValueRW.unitReleased = true;
                }

                if (elapsedSeconds >= revealAtSeconds + tuning.smokeLingerSeconds)
                    caravanExitSequenceEnabled.ValueRW = false;
                continue;
            }

            if (caravanDoors.ValueRO.phase != CaravanDoorPhase.Open)
            {
                CaravanDoorControl.RequestOpen(ref caravanDoors.ValueRW);
                continue;
            }

            if (!exitSequence.ValueRO.unitReleased)
            {
                float3 interiorPoint = caravanPose.TransformPoint(caravan.interiorStartLocalOffset);
                pendingSpawns.Add(new PendingMinionSpawn
                {
                    unitType = exitSequence.ValueRO.unitType,
                    position = interiorPoint,
                    walksOutOfCaravan = true,
                    caravanExitWalker = new CaravanExitWalker
                    {
                        interiorPoint = interiorPoint,
                        tailgatePoint = caravanPose.TransformPoint(caravan.tailgateEdgeLocalOffset),
                        landingPoint = caravanPose.TransformPoint(caravan.hopLandingLocalOffset),
                        walkOutSeconds = tuning.walkOutSeconds,
                        hopSeconds = tuning.hopSeconds,
                        hopArcHeight = tuning.hopArcHeight,
                    },
                });
                exitSequence.ValueRW.unitReleased = true;
                exitSequence.ValueRW.elapsedSeconds = 0f;
                continue;
            }

            exitSequence.ValueRW.elapsedSeconds += deltaTime;
            if (exitSequence.ValueRO.elapsedSeconds >= tuning.walkOutSeconds + tuning.hopSeconds + WALK_OUT_SETTLE_SECONDS)
                caravanExitSequenceEnabled.ValueRW = false;
        }
    }

    private static bool Crossed(float previousSeconds, float elapsedSeconds, float markSeconds)
    {
        return previousSeconds < markSeconds && elapsedSeconds >= markSeconds;
    }
}
