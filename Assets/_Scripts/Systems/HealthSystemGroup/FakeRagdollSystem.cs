using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Drives fake ragdoll each frame. No auto-disable — ragdoll stays active while Dead
/// is enabled. FakeRagdollReviveSystem handles cleanup when the unit is revived.
///
/// Flail naturally settles via JOINT_DAMPING without any timer logic, eliminating
/// the "spinning after timer" bug caused by ECB gaps.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial struct FakeRagdollSystem : ISystem
{
    // How fast the visual child lerps toward MAX_TILT_DEG. Higher = snappier fall.
    private const float BODY_FALL_SPEED  = 3.5f;
    // Target Z tilt in degrees. 90 = fully flat.
    private const float MAX_TILT_DEG    = 88f;
    // Joint angular velocity decay rate. Higher = arms settle sooner.
    private const float JOINT_DAMPING   = 3.0f;
    // Max degrees a joint can swing from its rest pose when the pivot is well above the floor.
    private const float MAX_JOINT_ANGLE = 75f;
    // Height above the per-joint clamp boundary at which full flail range is allowed.
    // Below this height the allowed range scales linearly to 0 at the boundary.
    // Increase if arms still clip near the floor; decrease if they freeze too early.
    private const float JOINT_HEIGHT_FULL_RANGE = 0.5f;

    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<LocalToWorld>   localToWorldLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup    = state.GetComponentLookup<LocalTransform>(true);
        localToWorldLookup = state.GetComponentLookup<LocalToWorld>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        localToWorldLookup.Update(ref state);

        float dt = SystemAPI.Time.DeltaTime;

        // ── Visual child: whole-body Z tilt ─────────────────────────────────
        foreach (var (transform, ragdoll) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<FakeRagdoll>>())
        {
            float targetAngle = MAX_TILT_DEG * ragdoll.ValueRO.fallSideSign;
            float angle = math.lerp(ragdoll.ValueRO.bodyZAngle, targetAngle, ragdoll.ValueRO.fallSpeed * dt);
            ragdoll.ValueRW.bodyZAngle = angle;

            quaternion tilt = quaternion.Euler(0f, 0f, math.radians(angle));
            transform.ValueRW.Rotation = math.mul(ragdoll.ValueRO.initialRotation, tilt);
            transform.ValueRW.Position.y = ragdoll.ValueRO.groundBuffer;
        }

        // ── Joint pivots: Z flail with angle and ground clamp ────────────────
        foreach (var (transform, joint, baseParent, jointEntity) in
            SystemAPI.Query<
                RefRW<LocalTransform>,
                RefRW<FakeRagdollJoint>,
                RefRO<BaseParent>>()
                    .WithEntityAccess())
        {
            joint.ValueRW.zAngularVelocity *= math.max(0f, 1f - JOINT_DAMPING * dt);

            float nextAngle = joint.ValueRO.currentZAngle + joint.ValueRO.zAngularVelocity * dt;

            // Ground-aware flail limit.
            // LocalToWorld is 1 frame stale (TransformSystemGroup ran before us with last frame's
            // body rotation), so we use a two-stage approach rather than a hard per-frame snap:
            //
            //   Stage 1 — dynamic limit: as the pivot descends toward the per-joint clamp
            //             boundary, linearly shrink MAX_JOINT_ANGLE to 0. Arms start losing
            //             range before they reach the floor so even with the 1-frame lag the
            //             flail naturally settles rather than punching through.
            //
            //   Stage 2 — hard lock: if the pivot IS at or below the clamp boundary, freeze
            //             completely. This is the backstop for edge cases.
            Entity rootEntity = baseParent.ValueRO.baseParentEntity;
            if (transformLookup.HasComponent(rootEntity) &&
                localToWorldLookup.HasComponent(jointEntity))
            {
                float rootWorldY  = transformLookup[rootEntity].Position.y;
                float jointWorldY = localToWorldLookup[jointEntity].Position.y;
                float clampY      = rootWorldY + joint.ValueRO.groundBuffer;

                // Stage 1: dynamic limit
                float heightAboveClamp = jointWorldY - clampY;
                float angleFraction    = math.saturate(heightAboveClamp / JOINT_HEIGHT_FULL_RANGE);
                float effectiveMax     = MAX_JOINT_ANGLE * angleFraction;

                if (nextAngle > effectiveMax)
                {
                    nextAngle = effectiveMax;
                    joint.ValueRW.zAngularVelocity = 0f;
                }
                else if (nextAngle < -effectiveMax)
                {
                    nextAngle = -effectiveMax;
                    joint.ValueRW.zAngularVelocity = 0f;
                }

                // Stage 2: hard lock at the boundary
                if (jointWorldY <= clampY)
                {
                    joint.ValueRW.zAngularVelocity = 0f;
                    nextAngle = 0f;
                }
            }
            else
            {
                // Fallback when lookups unavailable: static clamp only
                nextAngle = math.clamp(nextAngle, -MAX_JOINT_ANGLE, MAX_JOINT_ANGLE);
            }

            joint.ValueRW.currentZAngle = nextAngle;

            quaternion spin = quaternion.Euler(0f, 0f, math.radians(nextAngle));
            transform.ValueRW.Rotation = math.mul(joint.ValueRO.initialLocalRotation, spin);
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
