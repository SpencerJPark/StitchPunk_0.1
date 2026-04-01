using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Drives ragdoll each frame. No auto-disable — stays active while Dead is enabled.
/// Ragdoll2DReviveSystem handles cleanup on revive.
///
/// Body: lerps visual child Z tilt toward ±MAX_TILT_DEG + authored tiltOffset.
/// Joints: move toward a pre-chosen target angle (selected from authored landing zones at death).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
public partial struct Ragdoll2DSystem : ISystem
{
    // Target Z tilt in degrees for the whole body. 90 = fully flat.
    private const float MAX_TILT_DEG   = 88f;
    // Arc launch physics
    private const float LAUNCH_GRAVITY = 20f;   // units/s² downward
    private const float LAUNCH_X_DRAG  = 2.5f;  // exponential sideways deceleration

    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);

        float dt = SystemAPI.Time.DeltaTime;

        // ── Root entity: arc launch physics ──────────────────────────────────
        // Runs first so the updated root position is available to the joint clamp
        // lookup this same frame (via transformLookup, which reads LocalTransform directly).
        foreach (var (transform, launch) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<Ragdoll2DLaunch>>())
        {
            launch.ValueRW.velocityX *= math.max(0f, 1f - LAUNCH_X_DRAG * dt);
            launch.ValueRW.velocityY -= LAUNCH_GRAVITY * dt;

            float3 pos = transform.ValueRO.Position;
            pos.x += launch.ValueRO.velocityX * dt;
            pos.y += launch.ValueRO.velocityY * dt;

            if (pos.y <= launch.ValueRO.groundY)
            {
                pos.y = launch.ValueRO.groundY;
                launch.ValueRW.velocityX = 0f;
                launch.ValueRW.velocityY = 0f;
            }

            transform.ValueRW.Position = pos;
        }

        // ── Visual child: whole-body Z tilt ─────────────────────────────────
        foreach (var (transform, ragdoll) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<Ragdoll2D>>())
        {
            float targetAngle = MAX_TILT_DEG * ragdoll.ValueRO.fallSideSign + ragdoll.ValueRO.tiltOffset;
            float current     = ragdoll.ValueRO.bodyZAngle;
            float step        = ragdoll.ValueRO.fallSpeed * dt;
            float diff        = targetAngle - current;
            float angle       = math.abs(diff) <= step ? targetAngle : current + math.sign(diff) * step;
            ragdoll.ValueRW.bodyZAngle = angle;

            quaternion tilt = quaternion.Euler(0f, 0f, math.radians(angle));
            transform.ValueRW.Rotation = math.mul(ragdoll.ValueRO.initialRotation, tilt);
            transform.ValueRW.Position.y = ragdoll.ValueRO.groundBuffer;
        }

        // ── Joint pivots: move toward authored target angle ──────────────────
        foreach (var (transform, joint) in
            SystemAPI.Query<RefRW<LocalTransform>, RefRW<Ragdoll2DJoint>>())
        {
            float current = joint.ValueRO.currentZAngle;
            float target  = joint.ValueRO.targetAngle;
            float next    = math.lerp(current, target, 1f - math.exp(-joint.ValueRO.settleSpeed * dt));

            joint.ValueRW.currentZAngle = next;

            quaternion spin = quaternion.Euler(0f, 0f, math.radians(next));
            transform.ValueRW.Rotation = math.mul(joint.ValueRO.initialLocalRotation, spin);
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
