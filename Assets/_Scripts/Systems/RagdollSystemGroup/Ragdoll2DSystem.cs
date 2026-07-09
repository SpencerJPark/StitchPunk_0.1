using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Drives the corpse ragdoll — one parallel job over ragdolling roots. Each root exclusively owns
/// its visual child and joint pivots, so child writes go through
/// [NativeDisableParallelForRestriction] lookups (disjoint per Execute).
///
///   ① FLIGHT — integrate the float3 launch velocity; ground height from a CollisionWorld raycast
///      (ledges/props aware, plus the corpse-stack offset); walls bounce via restitution.
///   ② FLAIL — each joint is a 1-segment pendulum in the character plane (angle + angular
///      velocity), driven by gravity and root-motion pseudo-forces; impacts kick it.
///   ③ SETTLE — grounded joints exp-lerp toward their authored landing-zone angle (the original
///      look, unchanged); the body tilt steps toward ±MAX_TILT_DEG at fallSpeed, with the
///      per-attack spin winding extra turns while airborne and settling to the nearest turn.
///
/// Once every angle is quiet the corpse sleeps: all dynamics skip, but the settled rotations are
/// still re-written each frame because ApplyPoseJob stomps every part LocalTransform
/// unconditionally. No auto-disable — active while Dead; Ragdoll2DReviveSystem cleans up on revive.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(RagdollSystemGroup))]
public partial struct Ragdoll2DSystem : ISystem
{
    private ComponentLookup<LocalTransform>  transformLookup;
    private ComponentLookup<Ragdoll2D>       ragdollLookup;
    private ComponentLookup<Ragdoll2DJoint>  jointLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<CorpseCells>();

        transformLookup = state.GetComponentLookup<LocalTransform>(false);
        ragdollLookup   = state.GetComponentLookup<Ragdoll2D>(false);
        jointLookup     = state.GetComponentLookup<Ragdoll2DJoint>(false);
    }

    // Not [BurstCompile] — fetches the managed CorpseCellSystem to register the reader handle
    // (the corpse-cell map bypasses ECS dependency tracking; the ECB-owner pattern applies).
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        ragdollLookup.Update(ref state);
        jointLookup.Update(ref state);

        RagdollSimConfig simConfig = SystemAPI.TryGetSingleton(out RagdollSimConfig authoredConfig)
            ? authoredConfig
            : DefaultSimConfig();

        CollisionWorld collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        CollisionFilter groundFilter = new CollisionFilter
        {
            BelongsTo    = ~0u,
            CollidesWith = (1u << ConstGameData.GROUND_LAYER) |
                           (1u << ConstGameData.STRUCTURES_LAYER) |
                           (1u << ConstGameData.OBJECTS_LAYER),
            GroupIndex   = 0,
        };
        CollisionFilter wallFilter = new CollisionFilter
        {
            BelongsTo    = ~0u,
            CollidesWith = (1u << ConstGameData.STRUCTURES_LAYER) |
                           (1u << ConstGameData.WALLS_LAYER),
            GroupIndex   = 0,
        };

        state.Dependency = new RagdollDriveJob
        {
            collisionWorld  = collisionWorld,
            corpseCells     = SystemAPI.GetSingleton<CorpseCells>().map,
            simConfig       = simConfig,
            groundFilter    = groundFilter,
            wallFilter      = wallFilter,
            deltaTime       = SystemAPI.Time.DeltaTime,
            transformLookup = transformLookup,
            ragdollLookup   = ragdollLookup,
            jointLookup     = jointLookup,
        }.ScheduleParallel(state.Dependency);

        // The map is cleared by CorpseCellSystem next frame — it must wait for this read.
        state.World.GetExistingSystemManaged<CorpseCellSystem>()
            .AddJobHandleForReader(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    // Mirrors the RagdollConfigSO field defaults — used until RagdollSimConfigAuthoring is baked.
    private static RagdollSimConfig DefaultSimConfig() => new RagdollSimConfig
    {
        gravity               = 20f,
        horizontalDrag        = 2.5f,
        defaultRestitution    = 0.3f,
        bounceMinSpeed        = 2f,
        groundRaycastDistance = 5f,
        landingImpulseScale   = 1f,
        flailDamping          = 1.5f,
        sleepAngularSpeedDeg  = 1f,
        corpseCellSize        = 1f,
        corpseStackOffset     = 0.15f,
        corpseStackMax        = 5,
    };
}

[BurstCompile]
public partial struct RagdollDriveJob : IJobEntity
{
    // Target Z tilt in degrees for the whole body. 90 = fully flat.
    private const float MAX_TILT_DEG        = 88f;
    // Ground probe starts this far above the root so a slightly-sunk corpse still finds ground.
    private const float GROUND_PROBE_UP     = 1f;
    // Wall probe overshoot beyond this frame's displacement.
    private const float WALL_SKIN           = 0.2f;
    // Flail swing clamp — keeps an over-kicked limb from windmilling through the body.
    private const float MAX_JOINT_SWING_DEG = 170f;
    // Grounded joints snap to their zone target inside this band (identical final look to v1).
    private const float SETTLE_SNAP_DEG     = 0.25f;

    [ReadOnly] public CollisionWorld                          collisionWorld;
    [ReadOnly] public NativeParallelMultiHashMap<int2, float> corpseCells;
    public RagdollSimConfig simConfig;
    public CollisionFilter  groundFilter;
    public CollisionFilter  wallFilter;
    public float            deltaTime;

    // Each Execute touches only its own root + children — disjoint writes across workers.
    [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> transformLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<Ragdoll2D>      ragdollLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<Ragdoll2DJoint> jointLookup;

    public void Execute(
        Entity                  rootEntity,
        ref Ragdoll2DLaunch     launch,
        in Ragdoll2DConfig      config,
        DynamicBuffer<BodyPart> parts)
    {
        if (launch.sleeping == 1)
        {
            WriteSettledPose(config, parts);
            return;
        }

        if (!transformLookup.HasComponent(rootEntity))
            return;

        // ── ① FLIGHT — root ballistics + collision ──────────────────────────
        float  impactSpeed    = 0f;   // > 0 on the frame the corpse hits ground or a wall
        float3 velocityBefore = launch.velocity;

        if (launch.airborne == 1)
        {
            LocalTransform rootTransform = transformLookup[rootEntity];
            float3 position = rootTransform.Position;
            float3 velocity = launch.velocity;

            velocity.y -= simConfig.gravity * deltaTime;
            float dragFactor = math.max(0f, 1f - simConfig.horizontalDrag * deltaTime);
            velocity.x *= dragFactor;
            velocity.z *= dragFactor;

            float3 displacement       = velocity * deltaTime;
            float  displacementLength = math.length(displacement);

            // Wall bounce — probe along the motion from mid-body height.
            if (displacementLength > 1e-5f)
            {
                float3 probeStart     = position + new float3(0f, 0.5f, 0f);
                float3 probeDirection = displacement / displacementLength;
                RaycastInput wallRay = new RaycastInput
                {
                    Start  = probeStart,
                    End    = probeStart + probeDirection * (displacementLength + WALL_SKIN),
                    Filter = wallFilter,
                };
                if (collisionWorld.CastRay(wallRay, out Unity.Physics.RaycastHit wallHit))
                {
                    impactSpeed = math.length(velocity);
                    velocity    = math.reflect(velocity, wallHit.SurfaceNormal) * launch.restitution;
                    if (math.length(new float2(velocity.x, velocity.z)) < simConfig.bounceMinSpeed)
                    {
                        velocity.x = 0f;
                        velocity.z = 0f;
                    }
                    displacement = velocity * deltaTime;
                }
            }

            position += displacement;

            // Ground contact — the real ground below (ledges, props), plus the corpse-pile height.
            if (velocity.y <= 0f)
            {
                RaycastInput groundRay = new RaycastInput
                {
                    Start  = position + new float3(0f, GROUND_PROBE_UP, 0f),
                    End    = position - new float3(0f, simConfig.groundRaycastDistance, 0f),
                    Filter = groundFilter,
                };
                if (collisionWorld.CastRay(groundRay, out Unity.Physics.RaycastHit groundHit))
                {
                    float groundY = groundHit.Position.y + StackHeight(position);
                    if (position.y <= groundY)
                    {
                        impactSpeed = math.max(impactSpeed, -velocity.y);
                        position.y  = groundY;

                        if (-velocity.y * launch.restitution > simConfig.bounceMinSpeed)
                        {
                            velocity.y  = -velocity.y * launch.restitution;
                            velocity.x *= launch.restitution;
                            velocity.z *= launch.restitution;
                        }
                        else
                        {
                            velocity        = float3.zero;
                            launch.airborne = 0;
                        }
                    }
                }
            }

            launch.velocity        = velocity;
            rootTransform.Position = position;
            transformLookup[rootEntity] = rootTransform;
        }

        // ── ② + ③ — body tilt/spin, joint flail, authored settle ────────────
        Entity visualRoot = config.visualRoot;
        if (!ragdollLookup.HasComponent(visualRoot) || !ragdollLookup.IsComponentEnabled(visualRoot))
            return;

        Ragdoll2D ragdoll     = ragdollLookup[visualRoot];
        bool      corpseQuiet = launch.airborne == 0;

        // Spin winds the tilt past full turns while airborne; settle to the nearest equivalent.
        float baseTargetAngle = MAX_TILT_DEG * ragdoll.fallSideSign + ragdoll.tiltOffset;
        float fullTurns       = math.round((ragdoll.bodyZAngle - baseTargetAngle) / 360f);
        float tiltTargetAngle = baseTargetAngle + fullTurns * 360f;

        if (launch.airborne == 1)
        {
            ragdoll.bodyZAngle += ragdoll.spin * deltaTime;
        }
        else
        {
            ragdoll.spin *= math.exp(-simConfig.flailDamping * deltaTime);
            if (math.abs(ragdoll.spin) < simConfig.sleepAngularSpeedDeg)
                ragdoll.spin = 0f;
        }

        float tiltStep       = ragdoll.fallSpeed * deltaTime;
        float tiltDifference = tiltTargetAngle - ragdoll.bodyZAngle;
        ragdoll.bodyZAngle = math.abs(tiltDifference) <= tiltStep
            ? tiltTargetAngle
            : ragdoll.bodyZAngle + math.sign(tiltDifference) * tiltStep;

        corpseQuiet &= ragdoll.spin == 0f && ragdoll.bodyZAngle == tiltTargetAngle;

        if (transformLookup.HasComponent(visualRoot))
        {
            LocalTransform visualTransform = transformLookup[visualRoot];
            visualTransform.Rotation = math.mul(
                ragdoll.initialRotation,
                quaternion.Euler(0f, 0f, math.radians(ragdoll.bodyZAngle)));
            visualTransform.Position.y = ragdoll.groundBuffer;
            transformLookup[visualRoot] = visualTransform;
        }

        ragdollLookup[visualRoot] = ragdoll;

        // While airborne, limbs feel gravity MINUS the anchor's own acceleration — in clean
        // freefall that cancels to ~zero (weightless flail), and bounces read as sharp kicks.
        float2 anchorAcceleration = launch.airborne == 1 && deltaTime > 1e-5f
            ? new float2(
                (launch.velocity.x - velocityBefore.x) / deltaTime,
                (launch.velocity.y - velocityBefore.y) / deltaTime)
            : float2.zero;

        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            if ((parts[partIndex].flags & BodyPartFlags.RagdollJoint) == 0) continue;

            Entity jointEntity = parts[partIndex].entity;
            if (jointEntity == Entity.Null) continue;
            if (!jointLookup.HasComponent(jointEntity)) continue;
            if (!jointLookup.IsComponentEnabled(jointEntity)) continue;

            Ragdoll2DJoint joint = jointLookup[jointEntity];

            // Pendulum flail: the limb tip hangs at (bodyZAngle + currentZAngle) from straight
            // down; gravity and the anchor pseudo-force accelerate it along the tangent.
            float tipAngleRad = math.radians(ragdoll.bodyZAngle + joint.currentZAngle) - math.PI * 0.5f;
            float2 tangent    = new float2(-math.sin(tipAngleRad), math.cos(tipAngleRad));
            float2 planarAcceleration  = new float2(0f, -simConfig.gravity) - anchorAcceleration;
            float  segmentLength       = math.max(joint.segmentLength, 0.05f);
            float  angularAcceleration = math.dot(planarAcceleration, tangent) / segmentLength; // rad/s²

            joint.angularVelocity += math.degrees(angularAcceleration)
                * joint.weight * ragdoll.flailIntensity * deltaTime;

            if (impactSpeed > 0f)
            {
                joint.angularVelocity += math.degrees(impactSpeed / segmentLength)
                    * simConfig.landingImpulseScale * joint.weight
                    * ragdoll.flailIntensity * ragdoll.fallSideSign;
            }

            joint.angularVelocity *= math.exp(-simConfig.flailDamping * deltaTime);

            if (launch.airborne == 1)
            {
                joint.currentZAngle = math.clamp(
                    joint.currentZAngle + joint.angularVelocity * deltaTime,
                    -MAX_JOINT_SWING_DEG, MAX_JOINT_SWING_DEG);
            }
            else
            {
                // Authored settle (identical formula to v1) with the flail ringing out on top.
                float settleBlend = 1f - math.exp(-joint.settleSpeed * deltaTime);
                joint.currentZAngle = math.lerp(joint.currentZAngle, joint.targetAngle, settleBlend)
                    + joint.angularVelocity * deltaTime;

                if (math.abs(joint.angularVelocity) < simConfig.sleepAngularSpeedDeg)
                    joint.angularVelocity = 0f;

                bool jointQuiet = joint.angularVelocity == 0f
                    && math.abs(joint.currentZAngle - joint.targetAngle) < SETTLE_SNAP_DEG;
                if (jointQuiet)
                    joint.currentZAngle = joint.targetAngle;

                corpseQuiet &= jointQuiet;
            }

            if (transformLookup.HasComponent(jointEntity))
            {
                LocalTransform jointTransform = transformLookup[jointEntity];
                jointTransform.Rotation = math.mul(
                    joint.initialLocalRotation,
                    quaternion.Euler(0f, 0f, math.radians(joint.currentZAngle)));
                transformLookup[jointEntity] = jointTransform;
            }

            jointLookup[jointEntity] = joint;
        }

        if (corpseQuiet)
            launch.sleeping = 1;
    }

    // Pile height at this XZ position: settled corpses already occupying the landing cell.
    private float StackHeight(float3 position)
    {
        if (!corpseCells.IsCreated || simConfig.corpseStackOffset <= 0f)
            return 0f;

        int2 cell = new int2(
            (int)math.floor(position.x / simConfig.corpseCellSize),
            (int)math.floor(position.z / simConfig.corpseCellSize));
        int corpseCount = corpseCells.CountValuesForKey(cell);
        return simConfig.corpseStackOffset * math.min(corpseCount, simConfig.corpseStackMax);
    }

    // Sleeping corpses: re-write the settled rotations only — ApplyPoseJob stomps every part
    // LocalTransform each frame, so the pose must be re-asserted even with the dynamics asleep.
    private void WriteSettledPose(in Ragdoll2DConfig config, DynamicBuffer<BodyPart> parts)
    {
        Entity visualRoot = config.visualRoot;
        if (ragdollLookup.HasComponent(visualRoot) && ragdollLookup.IsComponentEnabled(visualRoot)
            && transformLookup.HasComponent(visualRoot))
        {
            Ragdoll2D ragdoll = ragdollLookup[visualRoot];
            LocalTransform visualTransform = transformLookup[visualRoot];
            visualTransform.Rotation = math.mul(
                ragdoll.initialRotation,
                quaternion.Euler(0f, 0f, math.radians(ragdoll.bodyZAngle)));
            visualTransform.Position.y = ragdoll.groundBuffer;
            transformLookup[visualRoot] = visualTransform;
        }

        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            if ((parts[partIndex].flags & BodyPartFlags.RagdollJoint) == 0) continue;

            Entity jointEntity = parts[partIndex].entity;
            if (jointEntity == Entity.Null) continue;
            if (!jointLookup.HasComponent(jointEntity)) continue;
            if (!jointLookup.IsComponentEnabled(jointEntity)) continue;
            if (!transformLookup.HasComponent(jointEntity)) continue;

            Ragdoll2DJoint joint = jointLookup[jointEntity];
            LocalTransform jointTransform = transformLookup[jointEntity];
            jointTransform.Rotation = math.mul(
                joint.initialLocalRotation,
                quaternion.Euler(0f, 0f, math.radians(joint.currentZAngle)));
            transformLookup[jointEntity] = jointTransform;
        }
    }
}
