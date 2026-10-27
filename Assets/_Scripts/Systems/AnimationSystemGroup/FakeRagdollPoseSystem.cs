using DotsAnimationToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Draws a knocked-down unit (FakeRagdollComponents.cs). The topple rides the billboarded Visual's
// PostTransformMatrix, a roll about its own facing axis at the feet, so VisualBillboardSystem's rotation
// write is never fought and the fall always reads sideways on screen. The limbs flail through their own
// LocalTransforms. On Restoring it puts the captured rest pose back and switches the component off.
[BurstCompile]
[UpdateInGroup(typeof(AnimationSystemGroup))]
[UpdateAfter(typeof(VisualBillboardSystem))]
public partial struct FakeRagdollPoseSystem : ISystem
{
    private const float FLAIL_RADIANS_PER_SECOND = 11f;
    private const float LYING_FLAIL_STRENGTH = 0.35f;
    private const float LYING_FLAIL_FADE_SECONDS = 0.5f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FakeRagdoll>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.QueryBuilder().WithAll<FakeRagdoll>().Build().IsEmpty) return;
        // Main-thread lookup writes on the Visual and limb entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();

        float3 flatCameraRight = new float3(1f, 0f, 0f);
        if (SystemAPI.TryGetSingleton(out AnimationToolkitCameraData cameraData))
        {
            float3 flatCameraForward = new float3(cameraData.forward.x, 0f, cameraData.forward.z);
            flatCameraRight = math.normalizesafe(math.cross(math.up(), flatCameraForward), flatCameraRight);
        }
        float elapsedSeconds = (float)SystemAPI.Time.ElapsedTime;
        EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);

        foreach ((RefRW<FakeRagdollRig> fakeRagdollRig, RefRO<FakeRagdoll> fakeRagdoll,
                     EnabledRefRW<FakeRagdoll> fakeRagdollEnabled, DynamicBuffer<FakeRagdollLimb> limbs)
                 in SystemAPI.Query<RefRW<FakeRagdollRig>, RefRO<FakeRagdoll>, EnabledRefRW<FakeRagdoll>,
                     DynamicBuffer<FakeRagdollLimb>>())
        {
            Entity visualEntity = fakeRagdollRig.ValueRO.visual;
            if (!SystemAPI.HasComponent<LocalTransform>(visualEntity)) continue;

            if (!fakeRagdollRig.ValueRO.restCaptured)
                CaptureRestPose(ref state, ref fakeRagdollRig.ValueRW, limbs);

            if (fakeRagdoll.ValueRO.phase == FakeRagdollPhase.Restoring)
            {
                ApplyPose(ref state, ref commandBuffer, visualEntity, fakeRagdollRig.ValueRO.visualRestMatrix, limbs, 0f, 0f);
                fakeRagdollRig.ValueRW.restCaptured = false;
                fakeRagdollEnabled.ValueRW = false;
                continue;
            }

            // A real toolkit ragdoll is posing the parts itself.
            if (fakeRagdoll.ValueRO.drivesToolkitRagdoll) continue;

            // Seen from the camera, +Z roll turns the body counter-clockwise: falling screen-right needs a negative roll.
            float screenSide = math.dot(fakeRagdoll.ValueRO.fallDirection, flatCameraRight) >= 0f ? -1f : 1f;
            float4x4 toppledMatrix = math.mul(float4x4.RotateZ(math.radians(fakeRagdoll.ValueRO.toppleDegrees * screenSide)),
                fakeRagdollRig.ValueRO.visualRestMatrix);

            float flailStrength = 0f;
            if (fakeRagdoll.ValueRO.phase == FakeRagdollPhase.Airborne)
                flailStrength = 1f;
            else if (fakeRagdoll.ValueRO.phase == FakeRagdollPhase.Lying)
                flailStrength = LYING_FLAIL_STRENGTH * math.saturate(1f - fakeRagdoll.ValueRO.phaseSeconds / LYING_FLAIL_FADE_SECONDS);

            ApplyPose(ref state, ref commandBuffer, visualEntity, toppledMatrix, limbs, flailStrength, elapsedSeconds);
        }

        commandBuffer.Playback(state.EntityManager);
        commandBuffer.Dispose();
    }

    private void CaptureRestPose(ref SystemState state, ref FakeRagdollRig fakeRagdollRig, DynamicBuffer<FakeRagdollLimb> limbs)
    {
        fakeRagdollRig.visualRestMatrix = SystemAPI.HasComponent<PostTransformMatrix>(fakeRagdollRig.visual)
            ? SystemAPI.GetComponent<PostTransformMatrix>(fakeRagdollRig.visual).Value
            : float4x4.identity;
        for (int limbIndex = 0; limbIndex < limbs.Length; limbIndex++)
        {
            FakeRagdollLimb limb = limbs[limbIndex];
            if (!SystemAPI.HasComponent<LocalTransform>(limb.limb)) continue;
            limb.restRotation = SystemAPI.GetComponent<LocalTransform>(limb.limb).Rotation;
            limbs[limbIndex] = limb;
        }
        fakeRagdollRig.restCaptured = true;
    }

    private void ApplyPose(ref SystemState state, ref EntityCommandBuffer commandBuffer, Entity visualEntity,
        float4x4 visualMatrix, DynamicBuffer<FakeRagdollLimb> limbs, float flailStrength, float elapsedSeconds)
    {
        if (SystemAPI.HasComponent<PostTransformMatrix>(visualEntity))
            SystemAPI.SetComponent(visualEntity, new PostTransformMatrix { Value = visualMatrix });
        else
            commandBuffer.AddComponent(visualEntity, new PostTransformMatrix { Value = visualMatrix });

        for (int limbIndex = 0; limbIndex < limbs.Length; limbIndex++)
        {
            FakeRagdollLimb limb = limbs[limbIndex];
            if (!SystemAPI.HasComponent<LocalTransform>(limb.limb)) continue;
            float swingDegrees = limb.flailDegrees * flailStrength
                * math.sin(elapsedSeconds * FLAIL_RADIANS_PER_SECOND + limb.flailPhase);
            LocalTransform limbTransform = SystemAPI.GetComponent<LocalTransform>(limb.limb);
            limbTransform.Rotation = math.mul(limb.restRotation, quaternion.RotateZ(math.radians(swingDegrees)));
            SystemAPI.SetComponent(limb.limb, limbTransform);
        }
    }
}
