// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>TransformSampleSystem</c>: writes each visible part's sampled
    /// <see cref="TargetPose"/> onto the transform components Entities Graphics renders from. Scale
    /// goes to <c>PostTransformMatrix</c>, never <c>LocalTransform.Scale</c> (pinned to 1) —
    /// <c>LocalTransform</c> can't express non-uniform or negative scale, and leaving the matrix
    /// unwritten is the classic "an authored scale curve silently does nothing" regression.
    /// </summary>
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

    // Iterates part entities, not actor roots — parts are the only entities carrying TargetPose,
    // so the query needs no tag to separate them.
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
