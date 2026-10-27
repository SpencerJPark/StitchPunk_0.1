using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

// Running units over (Caravan_System.md CV-D6). Every unit the moving caravan touches is shoved out of its path;
// above minimumImpactSpeed anyone but the player's own minions also takes speed-scaled damage and a stun, and a
// lethal hit launches the ragdoll along the shove. Each hit costs the caravan a little health and speed.
// A sourceless DamageBus producer: main-thread Enqueue before AttackRequestSystem, the HazardZoneSystem slot.
[BurstCompile]
[UpdateInGroup(typeof(CombatExecutionSystemGroup))]
[UpdateBefore(typeof(AttackRequestSystem))]
public partial struct CaravanCrashSystem : ISystem
{
    // TagManager layer "Units".
    private const int UNITS_PHYSICS_LAYER = 6;
    // Roughly a unit capsule's mid-height, so the crash box meets the capsule, not the floor.
    private const float CRASH_BOX_HEIGHT = 0.9f;
    // Below this the caravan is creeping, not ramming: nothing is hit at all.
    private const float CREEPING_SPEED = 0.5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Caravan>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<DamageBus>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.QueryBuilder().WithAll<CaravanDriving>().Build().IsEmpty) return;
        // Main-thread lookup reads/writes on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();
        CollisionWorld collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        CollisionFilter unitsOnlyFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = 1u << UNITS_PHYSICS_LAYER,
        };
        NativeQueue<DamageEvent> damageQueue = SystemAPI.GetSingleton<DamageBus>().raw;
        NativeList<DistanceHit> unitHits = new NativeList<DistanceHit>(32, Allocator.Temp);
        EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        double elapsedTime = SystemAPI.Time.ElapsedTime;

        foreach ((RefRO<LocalTransform> caravanTransform, RefRW<CaravanDriveState> driveState,
                     RefRO<CaravanDriveTuning> driveTuning, RefRO<CaravanCrashTuning> crashTuning,
                     RefRO<CaravanDriving> caravanDriving, DynamicBuffer<CaravanRecentHit> recentHits,
                     RefRW<Health> caravanHealth, Entity caravanEntity)
                 in SystemAPI.Query<RefRO<LocalTransform>, RefRW<CaravanDriveState>, RefRO<CaravanDriveTuning>,
                         RefRO<CaravanCrashTuning>, RefRO<CaravanDriving>, DynamicBuffer<CaravanRecentHit>, RefRW<Health>>()
                     .WithEntityAccess())
        {
            for (int hitIndex = recentHits.Length - 1; hitIndex >= 0; hitIndex--)
            {
                if (elapsedTime - recentHits[hitIndex].hitTime > crashTuning.ValueRO.rehitCooldownSeconds)
                    recentHits.RemoveAt(hitIndex);
            }

            float forwardSpeed = driveState.ValueRO.forwardSpeed;
            float impactSpeed = math.abs(forwardSpeed);
            if (impactSpeed < CREEPING_SPEED) continue;

            LocalTransform caravanPose = caravanTransform.ValueRO;
            CaravanDriveTuning tuning = driveTuning.ValueRO;
            CaravanCrashTuning crash = crashTuning.ValueRO;
            float3 forward = math.rotate(caravanPose.Rotation, new float3(0f, 0f, 1f));
            float3 right = math.rotate(caravanPose.Rotation, new float3(1f, 0f, 0f));
            float3 crashBoxRear = caravanPose.TransformPoint(new float3(0f, CRASH_BOX_HEIGHT, tuning.rearBumperLocalZ + tuning.halfWidth));
            float3 crashBoxFront = caravanPose.TransformPoint(new float3(0f, CRASH_BOX_HEIGHT, tuning.frontBumperLocalZ - tuning.halfWidth));

            unitHits.Clear();
            collisionWorld.OverlapCapsule(crashBoxRear, crashBoxFront, tuning.halfWidth, ref unitHits, unitsOnlyFilter,
                QueryInteraction.IgnoreTriggers);

            Entity driverEntity = caravanDriving.ValueRO.driver;
            int unitsHit = 0;
            int unitsHurt = 0;
            for (int overlapIndex = 0; overlapIndex < unitHits.Length; overlapIndex++)
            {
                Entity unitEntity = unitHits[overlapIndex].Entity;
                if (unitEntity == caravanEntity || unitEntity == driverEntity) continue;
                if (SystemAPI.HasComponent<Player>(unitEntity)) continue;
                if (!SystemAPI.HasComponent<Health>(unitEntity)) continue;
                if (SystemAPI.HasComponent<Dead>(unitEntity) && SystemAPI.IsComponentEnabled<Dead>(unitEntity)) continue;
                if (WasHitRecently(recentHits, unitEntity)) continue;

                // The body's pose, not LocalTransform: this query already holds the caravan's LocalTransform.
                float3 unitPosition = collisionWorld.Bodies[unitHits[overlapIndex].RigidBodyIndex].WorldFromBody.pos;
                float sideOfCaravan = math.dot(unitPosition - caravanPose.Position, right) >= 0f ? 1f : -1f;
                float3 shoveDirection = math.normalizesafe(right * sideOfCaravan + forward * math.sign(forwardSpeed) * 0.5f);
                QueueShove(ref state, ref commandBuffer, unitEntity,
                    shoveDirection * (crash.shoveDistance / math.max(crash.shoveSeconds, 0.01f)), crash.shoveSeconds);

                recentHits.Add(new CaravanRecentHit { unit = unitEntity, hitTime = elapsedTime });
                unitsHit++;

                // Your own minions are only pushed aside (CV-D6, CV-Q5).
                bool isPlayersMinion = SystemAPI.HasComponent<Minion>(unitEntity) && SystemAPI.IsComponentEnabled<Minion>(unitEntity);
                if (isPlayersMinion || impactSpeed < crash.minimumImpactSpeed) continue;

                damageQueue.Enqueue(new DamageEvent
                {
                    targetEntity    = unitEntity,
                    sourceEntity    = Entity.Null,
                    damageSource    = DamageSource.Vehicle,
                    damageAmount    = (int)math.ceil(impactSpeed * crash.damagePerMetrePerSecond),
                    distance        = 0f,
                    ragdollForce    = 1.5f,
                    launchForceY    = 2.5f + impactSpeed * 0.35f,
                    launchForceX    = 3f + impactSpeed * 1.1f,
                    flailIntensity  = 1.5f,
                    spin            = 360f,
                    damageBehaviour = DamageBehaviour.SinlgeTarget,
                    // Launch goes away from sourcePosition, so put the source behind the unit along the shove.
                    sourcePosition  = unitPosition - shoveDirection * 1.5f,
                    range           = 0f,
                });
                QueueStun(ref state, ref commandBuffer, unitEntity, impactSpeed * crash.stunSecondsPerMetrePerSecond);
                unitsHurt++;
            }

            if (unitsHit == 0) continue;
            driveState.ValueRW.forwardSpeed = forwardSpeed * math.pow(1f - crash.speedLossPerHit, unitsHit);
            caravanHealth.ValueRW.healthAmount = math.max(0, caravanHealth.ValueRO.healthAmount - crash.selfDamagePerHit * unitsHurt);
        }

        commandBuffer.Playback(state.EntityManager);
        commandBuffer.Dispose();
        unitHits.Dispose();
    }

    private static bool WasHitRecently(DynamicBuffer<CaravanRecentHit> recentHits, Entity unitEntity)
    {
        for (int hitIndex = 0; hitIndex < recentHits.Length; hitIndex++)
        {
            if (recentHits[hitIndex].unit == unitEntity) return true;
        }
        return false;
    }

    // Units don't bake these; the first knock adds them and later knocks re-arm them.
    private void QueueShove(ref SystemState state, ref EntityCommandBuffer commandBuffer, Entity unitEntity,
        float3 shoveVelocity, float shoveSeconds)
    {
        ShoveMotion shoveMotion = new ShoveMotion { velocity = shoveVelocity, remainingSeconds = shoveSeconds };
        if (SystemAPI.HasComponent<ShoveMotion>(unitEntity))
        {
            commandBuffer.SetComponent(unitEntity, shoveMotion);
            commandBuffer.SetComponentEnabled<ShoveMotion>(unitEntity, true);
        }
        else
        {
            commandBuffer.AddComponent(unitEntity, shoveMotion);
        }
    }

    private void QueueStun(ref SystemState state, ref EntityCommandBuffer commandBuffer, Entity unitEntity, float stunSeconds)
    {
        if (stunSeconds <= 0f) return;
        StunnedState stunnedState = new StunnedState { duration = stunSeconds };
        if (SystemAPI.HasComponent<StunnedState>(unitEntity))
        {
            commandBuffer.SetComponent(unitEntity, stunnedState);
            commandBuffer.SetComponentEnabled<StunnedState>(unitEntity, true);
        }
        else
        {
            commandBuffer.AddComponent(unitEntity, stunnedState);
        }
    }
}
