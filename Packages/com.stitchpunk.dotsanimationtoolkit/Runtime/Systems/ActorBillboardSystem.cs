// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Rotates each billboarded actor's root so the whole rig faces the viewer as one
    /// (architecture section 6.3, amendment A41).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absorbed and generalised from the host game's own <c>BillboardSystem</c>, which the audit
    /// recommended keeping and §13.1 briefly decided to delete in favour of the shader path. A41
    /// withdrew that: rotating each quad about its own pivot fans a layered cutout character apart,
    /// and only a root rotation keeps the composition rigid.
    /// </para>
    /// <para>
    /// <strong>Runs in the presentation group, after the pose is applied.</strong>
    /// <c>TransformApplySystem</c> writes each <em>part's</em> local transform; this writes the
    /// <em>root's</em>. They do not contend — parts are children, so the root rotation composes over
    /// their local poses exactly as a parent transform should — but the order is fixed anyway so the
    /// billboard never lands a frame behind the pose it is turning.
    /// </para>
    /// <para>
    /// <strong>Gated on <see cref="AnimVisible"/>.</strong> Turning an off-screen actor to face a
    /// camera that cannot see it is the definition of presentation work worth skipping, and the
    /// first visible frame recomputes it from scratch — there is no state to catch up.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformApplySystem))]
    [BurstCompile]
    public partial struct ActorBillboardSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorBillboard>();

            // Without a camera there is nothing to face. The host writes this singleton; the package
            // never reads a Camera, because it cannot know which of a host's cameras matters.
            state.RequireForUpdate<AnimationToolkitCameraData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            AnimationToolkitCameraData cameraData = SystemAPI.GetSingleton<AnimationToolkitCameraData>();

            FaceCameraJob faceJob = new FaceCameraJob
            {
                cameraPosition = cameraData.position,
                cameraForward = cameraData.forward
            };
            state.Dependency = faceJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Applies one actor's billboard rule to its root rotation.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct FaceCameraJob : IJobEntity
    {
        public float3 cameraPosition;
        public float3 cameraForward;

        /// <summary>Below this a direction is degenerate and the transform is left untouched.</summary>
        private const float DirectionEpsilon = 1e-6f;

        private void Execute(ref LocalTransform localTransform, in ActorBillboard billboard)
        {
            if (billboard.mode == BillboardMode.Off)
            {
                return;
            }

            float3 facing = ResolveFacing(billboard.mode, localTransform.Position);
            if (math.lengthsq(facing) < DirectionEpsilon)
            {
                return;
            }

            if (billboard.mode == BillboardMode.Upright)
            {
                // Flattening the facing vector is what restricts the turn to world Y, so an upright
                // actor faces the camera without ever leaning toward or away from it.
                facing.y = 0f;
                if (math.lengthsq(facing) < DirectionEpsilon)
                {
                    return;
                }
            }

            quaternion target = quaternion.LookRotationSafe(math.normalize(facing), math.up());

            if (billboard.mode == BillboardMode.FrozenYaw)
            {
                target = ApplyFrozenYaw(target, billboard.frozenYaw);
            }

            localTransform.Rotation = target;
        }

        /// <summary>
        /// The direction the actor should look along, pointing from the actor toward the viewer.
        /// </summary>
        private float3 ResolveFacing(BillboardMode mode, float3 actorPosition)
        {
            // Screen-aligned takes the camera's forward negated, so every actor ends up with the
            // same rotation (A39). A host that never writes the forward falls back to spherical
            // rather than collapsing — a different look beats a degenerate one.
            if (mode == BillboardMode.ScreenAligned && math.lengthsq(cameraForward) >= DirectionEpsilon)
            {
                return -cameraForward;
            }
            return cameraPosition - actorPosition;
        }

        /// <summary>
        /// Substitutes an authored yaw while keeping the camera-derived pitch.
        /// </summary>
        /// <remarks>
        /// The decomposition is lifted from the host's own system, which had it right: split the
        /// camera-facing target into yaw and pitch, discard the yaw, and rebuild with the frozen one.
        /// Simply writing a yaw rotation instead would leave a corpse staring flat ahead regardless
        /// of where the camera sits above it.
        /// </remarks>
        private static quaternion ApplyFrozenYaw(quaternion cameraFacing, float frozenYaw)
        {
            float3 targetForward = math.mul(cameraFacing, math.forward());
            float3 flatForward = new float3(targetForward.x, 0f, targetForward.z);

            quaternion frozen = quaternion.RotateY(frozenYaw);
            if (math.lengthsq(flatForward) < DirectionEpsilon)
            {
                // Camera directly overhead: there is no yaw to strip, so the frozen yaw is the whole
                // answer rather than a component of it.
                return frozen;
            }

            quaternion cameraYaw = quaternion.LookRotationSafe(math.normalize(flatForward), math.up());
            quaternion pitchOnly = math.mul(math.inverse(cameraYaw), cameraFacing);
            return math.mul(frozen, pitchOnly);
        }
    }
}
