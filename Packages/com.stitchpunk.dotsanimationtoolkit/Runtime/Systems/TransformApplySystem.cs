// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Writes each visible part's sampled <see cref="TargetPose"/> onto the transform components
    /// Entities.Graphics actually renders from (architecture section 5.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Scale goes to <c>PostTransformMatrix</c>, not to <c>LocalTransform.Scale</c>.</strong>
    /// <c>LocalTransform</c> carries a single uniform float, so it can express neither the
    /// non-uniform x/y scale a 2D cutout rig needs nor the negative scale that flips a part. Writing
    /// scale there and leaving the matrix alone is precisely the host game's dead-scale regression
    /// (audit §2.1): its apply system forced <c>Scale = 1</c> and never wrote
    /// <c>PostTransformMatrix</c>, so every authored scale curve in the project silently did
    /// nothing. Both halves are written here, every frame, for exactly that reason.
    /// </para>
    /// <para>
    /// <c>LocalTransform.Scale</c> is pinned to 1 rather than left untouched: the matrix is the
    /// whole scale channel, so any residual uniform scale would multiply on top of it and
    /// double-apply.
    /// </para>
    /// <para>
    /// <strong>Nothing here reads a clip.</strong> This system is a pure copy from the pose
    /// <c>TransformSampleSystem</c> produced, which is what keeps the single-sampler guarantee of
    /// section 5.11 intact — a second place that interpreted clip data would be a second sampler.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitPresentationSystemGroup))]
    [UpdateAfter(typeof(TransformSampleSystem))]
    [BurstCompile]
    public partial struct TransformApplySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TargetPose>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ApplyTargetPoseJob applyJob = new ApplyTargetPoseJob();
            state.Dependency = applyJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>
    /// Copies one part's sampled pose onto its transform.
    /// </summary>
    /// <remarks>
    /// Iterates part entities, not actor roots — parts are the only entities carrying
    /// <see cref="TargetPose"/>, so the query needs no tag to separate them. Each part writes only
    /// its own components, so this parallelises with nothing to coordinate.
    /// </remarks>
    [BurstCompile]
    [WithAll(typeof(AnimVisible))]
    internal partial struct ApplyTargetPoseJob : IJobEntity
    {
        private void Execute(
            in TargetPose pose,
            ref LocalTransform localTransform,
            ref PostTransformMatrix postTransformMatrix)
        {
            localTransform.Position = pose.localPosition;
            // Euler in ZXY, matching UnityEngine.Transform.eulerAngles, so an angle typed in this
            // toolkit's inspector means the same thing it would in Unity's.
            localTransform.Rotation = quaternion.Euler(pose.rotation);
            localTransform.Scale = 1f;
            postTransformMatrix.Value = float4x4.Scale(pose.scale);
        }
    }
}
