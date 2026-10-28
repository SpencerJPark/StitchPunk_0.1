using DotsAnimationToolkit;
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
        state.RequireForUpdate<VehicleLibrary>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<DamageBus>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.QueryBuilder().WithAll<CaravanDriving>().Build().IsEmpty) return;
        // Main-thread lookup reads/writes on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();
        BlobAssetReference<VehicleLibraryBlob> vehicleLibrary = SystemAPI.GetSingleton<VehicleLibrary>().library;
        if (!vehicleLibrary.IsCreated) return;
        // Only heavy units (UnitSO.isHeavy) eat momentum; without the library nobody counts as heavy.
        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.TryGetSingleton(out UnitDataLibrary unitDataLibrary)
            ? unitDataLibrary.library
            : default;
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

        foreach ((RefRO<LocalTransform> caravanTransform, RefRW<CaravanDriveState> driveState, RefRO<Vehicle> vehicle,
                     RefRO<CaravanDriving> caravanDriving, DynamicBuffer<CaravanRecentHit> recentHits,
                     RefRW<Health> caravanHealth, Entity caravanEntity)
                 in SystemAPI.Query<RefRO<LocalTransform>, RefRW<CaravanDriveState>, RefRO<Vehicle>,
                         RefRO<CaravanDriving>, DynamicBuffer<CaravanRecentHit>, RefRW<Health>>()
                     .WithEntityAccess())
        {
            int vehicleIndex = (int)vehicle.ValueRO.vehicleType;
            if (vehicleIndex <= 0 || vehicleIndex >= vehicleLibrary.Value.vehicles.Length) continue;
            ref VehicleProfileBlob profile = ref vehicleLibrary.Value.vehicles[vehicleIndex];

            for (int hitIndex = recentHits.Length - 1; hitIndex >= 0; hitIndex--)
            {
                if (elapsedTime - recentHits[hitIndex].hitTime > profile.rehitCooldownSeconds)
                    recentHits.RemoveAt(hitIndex);
            }

            float forwardSpeed = driveState.ValueRO.forwardSpeed;
            float impactSpeed = math.abs(forwardSpeed);
            if (impactSpeed < CREEPING_SPEED) continue;

            LocalTransform caravanPose = caravanTransform.ValueRO;
            float3 forward = math.rotate(caravanPose.Rotation, new float3(0f, 0f, 1f));
            float3 right = math.rotate(caravanPose.Rotation, new float3(1f, 0f, 0f));
            float3 crashBoxRear = caravanPose.TransformPoint(new float3(0f, CRASH_BOX_HEIGHT, profile.rearBumperLocalZ + profile.halfWidth));
            float3 crashBoxFront = caravanPose.TransformPoint(new float3(0f, CRASH_BOX_HEIGHT, profile.frontBumperLocalZ - profile.halfWidth));

            unitHits.Clear();
            collisionWorld.OverlapCapsule(crashBoxRear, crashBoxFront, profile.halfWidth, ref unitHits, unitsOnlyFilter,
                QueryInteraction.IgnoreTriggers);

            Entity driverEntity = caravanDriving.ValueRO.driver;
            int unitsHit = 0;
            int heavyUnitsHit = 0;
            int unitsHurt = 0;
            for (int overlapIndex = 0; overlapIndex < unitHits.Length; overlapIndex++)
            {
                Entity unitEntity = unitHits[overlapIndex].Entity;
                if (unitEntity == caravanEntity || unitEntity == driverEntity) continue;
                if (SystemAPI.HasComponent<Player>(unitEntity)) continue;
                if (!SystemAPI.HasComponent<Health>(unitEntity)) continue;
                if (SystemAPI.HasComponent<Dead>(unitEntity) && SystemAPI.IsComponentEnabled<Dead>(unitEntity)) continue;
                if (WasHitRecently(recentHits, unitEntity)) continue;
                // Already flying or lying from an earlier hit.
                if (SystemAPI.HasComponent<FakeRagdoll>(unitEntity) && SystemAPI.IsComponentEnabled<FakeRagdoll>(unitEntity)) continue;

                // The body's pose, not LocalTransform: this query already holds the caravan's LocalTransform.
                float3 unitPosition = collisionWorld.Bodies[unitHits[overlapIndex].RigidBodyIndex].WorldFromBody.pos;
                float sideOfCaravan = math.dot(unitPosition - caravanPose.Position, right) >= 0f ? 1f : -1f;
                float3 shoveDirection = math.normalizesafe(right * sideOfCaravan + forward * math.sign(forwardSpeed) * 0.5f);

                recentHits.Add(new CaravanRecentHit { unit = unitEntity, hitTime = elapsedTime });
                unitsHit++;
                if (IsHeavyUnit(ref state, unitEntity, unitLibrary)) heavyUnitsHit++;

                // Above impact speed anyone with a rig is knocked flying and gets back up (CV-D15); below it, or
                // with no rig to fall over, they're pushed aside.
                bool knockedOver = impactSpeed >= profile.minimumImpactSpeed
                    && TryKnockOver(ref state, unitEntity, shoveDirection, impactSpeed, profile.stunSecondsPerMetrePerSecond);
                if (!knockedOver)
                    QueueShove(ref state, ref commandBuffer, unitEntity,
                        shoveDirection * (profile.shoveDistance / math.max(profile.shoveSeconds, 0.01f)), profile.shoveSeconds);

                // Your own minions take no damage (CV-D6, CV-D15).
                bool isPlayersMinion = SystemAPI.HasComponent<Minion>(unitEntity) && SystemAPI.IsComponentEnabled<Minion>(unitEntity);
                if (isPlayersMinion || impactSpeed < profile.minimumImpactSpeed) continue;

                damageQueue.Enqueue(new DamageEvent
                {
                    targetEntity    = unitEntity,
                    sourceEntity    = Entity.Null,
                    damageSource    = DamageSource.Vehicle,
                    damageAmount    = (int)math.ceil(impactSpeed * profile.damagePerMetrePerSecond),
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
                // Lying on the ground is the stun for a knocked-over unit.
                if (!knockedOver)
                    QueueStun(ref state, ref commandBuffer, unitEntity, impactSpeed * profile.stunSecondsPerMetrePerSecond);
                unitsHurt++;
            }

            if (unitsHit == 0) continue;
            // Ploughing through light units costs nothing; a heavy one is like hitting a wall in part.
            driveState.ValueRW.forwardSpeed = forwardSpeed * math.pow(1f - profile.speedLossPerHeavyUnit, heavyUnitsHit);
            caravanHealth.ValueRW.healthAmount = math.max(0, caravanHealth.ValueRO.healthAmount - profile.selfDamagePerHit * unitsHurt);
        }

        commandBuffer.Playback(state.EntityManager);
        commandBuffer.Dispose();
        unitHits.Dispose();
    }

    // Faster hits throw further and keep the unit down longer. A unit with real toolkit ragdoll bodies is
    // launched through the toolkit; FakeRagdoll then only times its stand-up.
    private bool TryKnockOver(ref SystemState state, Entity unitEntity, float3 shoveDirection, float impactSpeed,
        float stunSecondsPerMetrePerSecond)
    {
        if (!SystemAPI.HasComponent<FakeRagdoll>(unitEntity)) return false;

        float3 launchVelocity = shoveDirection * (2f + impactSpeed * 0.6f) + new float3(0f, 2.5f + impactSpeed * 0.3f, 0f);
        float lieSeconds = 0.8f + impactSpeed * stunSecondsPerMetrePerSecond;
        bool hasToolkitRagdoll = SystemAPI.HasBuffer<RagdollBody>(unitEntity)
            && SystemAPI.GetBuffer<RagdollBody>(unitEntity).Length > 0
            && SystemAPI.HasComponent<RagdollActor>(unitEntity);

        FakeRagdoll fakeRagdoll = SystemAPI.GetComponent<FakeRagdoll>(unitEntity);
        FakeRagdollControl.Knock(ref fakeRagdoll, SystemAPI.GetComponent<LocalTransform>(unitEntity).Position,
            launchVelocity, lieSeconds, hasToolkitRagdoll);
        SystemAPI.SetComponent(unitEntity, fakeRagdoll);
        SystemAPI.SetComponentEnabled<FakeRagdoll>(unitEntity, true);

        if (hasToolkitRagdoll)
        {
            // Same impulse convention as RagdollLaunchInitSystem: authored as a velocity, so divide by the root's invMass.
            DynamicBuffer<RagdollBody> ragdollBodies = SystemAPI.GetBuffer<RagdollBody>(unitEntity);
            float rootInverseMass = ragdollBodies[0].parameters.invMass;
            if (SystemAPI.HasComponent<RagdollLaunch>(unitEntity))
            {
                SystemAPI.SetComponent(unitEntity, new RagdollLaunch
                {
                    worldImpulse = rootInverseMass > 0f ? launchVelocity / rootInverseMass : launchVelocity,
                    worldPoint = ragdollBodies[0].state.position,
                    worldTorque = float3.zero,
                });
                SystemAPI.SetComponentEnabled<RagdollLaunch>(unitEntity, true);
            }
            SystemAPI.SetComponentEnabled<RagdollActor>(unitEntity, true);
        }
        return true;
    }

    private bool IsHeavyUnit(ref SystemState state, Entity unitEntity, BlobAssetReference<UnitLibraryBlob> unitLibrary)
    {
        if (!unitLibrary.IsCreated || !SystemAPI.HasComponent<UnitData>(unitEntity)) return false;
        int unitIndex = unitLibrary.Value.FindByUnitType(SystemAPI.GetComponent<UnitData>(unitEntity).unitType);
        return unitIndex >= 0 && unitLibrary.Value.units[unitIndex].isHeavy;
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
