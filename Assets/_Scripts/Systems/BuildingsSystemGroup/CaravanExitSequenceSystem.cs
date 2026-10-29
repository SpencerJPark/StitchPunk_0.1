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
                     DynamicBuffer<CaravanSummonExitDoor> exitDoors, RefRW<CaravanShake> caravanShake,
                     RefRW<CaravanPresentationEvents> presentationEvents, RefRO<CaravanExitTuning> exitTuning,
                     DynamicBuffer<PendingMinionSpawn> pendingSpawns, Entity caravanEntity)
                 in SystemAPI.Query<RefRW<CaravanExitSequence>, EnabledRefRW<CaravanExitSequence>, DynamicBuffer<CaravanSummonExitDoor>,
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
                if (exitSequence.ValueRO.elapsedSeconds <= 0f)
                {
                    OverrideExitDoors(ref state, exitDoors, DoorOverride.HoldShut);
                    if (!AllExitDoorsAre(ref state, exitDoors, DoorPhase.Closed)) continue;
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
                    OverrideExitDoors(ref state, exitDoors, DoorOverride.HoldOpen);
                    for (int doorIndex = 0; doorIndex < exitDoors.Length; doorIndex++)
                        if (SystemAPI.HasComponent<Door>(exitDoors[doorIndex].door))
                            SystemAPI.GetComponentRW<Door>(exitDoors[doorIndex].door).ValueRW.burstRequested = true;
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
                {
                    OverrideExitDoors(ref state, exitDoors, DoorOverride.None);
                    caravanExitSequenceEnabled.ValueRW = false;
                }
                continue;
            }

            OverrideExitDoors(ref state, exitDoors, DoorOverride.HoldOpen);
            if (!AllExitDoorsAre(ref state, exitDoors, DoorPhase.Open)) continue;

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
            {
                OverrideExitDoors(ref state, exitDoors, DoorOverride.None);
                caravanExitSequenceEnabled.ValueRW = false;
            }
        }
    }

    // While set, this beats the doors' zones; None hands them back.
    private void OverrideExitDoors(ref SystemState state, DynamicBuffer<CaravanSummonExitDoor> exitDoors, DoorOverride doorOverride)
    {
        for (int doorIndex = 0; doorIndex < exitDoors.Length; doorIndex++)
        {
            Entity doorEntity = exitDoors[doorIndex].door;
            if (SystemAPI.HasComponent<Door>(doorEntity))
                SystemAPI.GetComponentRW<Door>(doorEntity).ValueRW.scriptOverride = doorOverride;
        }
    }

    // True with no exit doors authored, so a caravan without doors still summons.
    private bool AllExitDoorsAre(ref SystemState state, DynamicBuffer<CaravanSummonExitDoor> exitDoors, DoorPhase phase)
    {
        for (int doorIndex = 0; doorIndex < exitDoors.Length; doorIndex++)
        {
            Entity doorEntity = exitDoors[doorIndex].door;
            if (SystemAPI.HasComponent<Door>(doorEntity) && SystemAPI.GetComponent<Door>(doorEntity).phase != phase) return false;
        }
        return true;
    }

    private static bool Crossed(float previousSeconds, float elapsedSeconds, float markSeconds)
    {
        return previousSeconds < markSeconds && elapsedSeconds >= markSeconds;
    }
}
