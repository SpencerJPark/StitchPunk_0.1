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
    // Max degrees a joint can swing from its rest pose (each direction).
    private const float MAX_JOINT_ANGLE = 75f;

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
            float angle = math.lerp(ragdoll.ValueRO.bodyZAngle, targetAngle, BODY_FALL_SPEED * dt);
            ragdoll.ValueRW.bodyZAngle = angle;

            quaternion tilt = quaternion.Euler(0f, 0f, math.radians(angle));
            transform.ValueRW.Rotation = math.mul(ragdoll.ValueRO.initialRotation, tilt);
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

            if (nextAngle > MAX_JOINT_ANGLE)
            {
                nextAngle = MAX_JOINT_ANGLE;
                joint.ValueRW.zAngularVelocity = 0f;
            }
            else if (nextAngle < -MAX_JOINT_ANGLE)
            {
                nextAngle = -MAX_JOINT_ANGLE;
                joint.ValueRW.zAngularVelocity = 0f;
            }

            // Ground clamp — compare the JOINT's world Y against root's world Y + buffer.
            // Root has no parent so LocalTransform.Position.y == world Y.
            Entity rootEntity = baseParent.ValueRO.baseParentEntity;
            if (transformLookup.HasComponent(rootEntity) &&
                localToWorldLookup.HasComponent(jointEntity))
            {
                float rootWorldY  = transformLookup[rootEntity].Position.y;
                float clampY      = rootWorldY + joint.ValueRO.groundBuffer;
                float jointWorldY = localToWorldLookup[jointEntity].Position.y;
                if (jointWorldY < clampY)
                {
                    joint.ValueRW.zAngularVelocity = 0f;
                    // Nudge angle back so the joint sits at the clamp boundary rather than clipping
                    nextAngle = joint.ValueRO.currentZAngle * 0.5f;
                }
            }

            joint.ValueRW.currentZAngle = nextAngle;

            quaternion spin = quaternion.Euler(0f, 0f, math.radians(nextAngle));
            transform.ValueRW.Rotation = math.mul(joint.ValueRO.initialLocalRotation, spin);
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
