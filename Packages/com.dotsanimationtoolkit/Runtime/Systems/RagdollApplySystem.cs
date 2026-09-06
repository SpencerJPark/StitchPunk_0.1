// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>RagdollSolveSystem</c>: writes every active ragdoll's solved pose back onto
    /// its nodes' <see cref="LocalTransform"/>. Must write every frame, even a sleeping one —
    /// <c>TransformApplySystem</c> stomps every visible part's <c>LocalTransform</c> unconditionally
    /// earlier in the frame, so skipping this write on a "nothing changed" sleeping frame lets that
    /// animated pose stand uncorrected and a settled ragdoll snaps back to its animated stance.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitRagdollSystemGroup))]
    [UpdateAfter(typeof(RagdollSolveSystem))]
    [BurstCompile]
    public partial struct RagdollApplySystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RagdollBody>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ApplyRagdollJob applyJob = new ApplyRagdollJob
            {
                localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(),
                parentLookup = SystemAPI.GetComponentLookup<Parent>(true)
            };
            state.Dependency = applyJob.ScheduleParallel(state.Dependency);
        }
    }

    // Parallel-safe despite the restriction disable: each actor's RagdollBody buffer names only
    // nodes in that same actor's own hierarchy, so different actors' Execute calls write disjoint
    // node entities even though the safety system cannot prove that statically.
    [BurstCompile]
    [WithAll(typeof(RagdollActor))]
    internal partial struct ApplyRagdollJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup;
        [ReadOnly] public ComponentLookup<Parent> parentLookup;

        private void Execute(in DynamicBuffer<RagdollBody> bodyElements)
        {
            for (int bodyIndex = 0; bodyIndex < bodyElements.Length; bodyIndex++)
            {
                RagdollBody body = bodyElements[bodyIndex];
                Entity node = body.node;
                if (!localTransformLookup.HasComponent(node))
                {
                    continue;
                }

                // Invert RagdollCaptureSystem's boxCenter fold-in to recover the node's own world pose.
                float3 nodeWorldPosition =
                    body.state.position - math.mul(body.state.orientation, body.parameters.boxCenter);
                quaternion nodeWorldRotation = body.state.orientation;

                // Bodies are written shallowest-first (RagdollBody's own ordering guarantee), so by
                // the time this reaches a child body, a parent that is itself a body already holds
                // this frame's solved LocalTransform, and this walk picks that up rather than a stale one.
                RagdollTransformMath.ComputeParentWorldTransform(
                    in node, in localTransformLookup, in parentLookup, out LocalTransform parentWorldTransform);

                float parentScale = parentWorldTransform.Scale;
                float3 localPosition = math.abs(parentScale) > 1e-8f
                    ? math.mul(math.inverse(parentWorldTransform.Rotation), nodeWorldPosition - parentWorldTransform.Position)
                        / parentScale
                    : float3.zero;
                quaternion localRotation = math.mul(math.inverse(parentWorldTransform.Rotation), nodeWorldRotation);

                // Scale is preserved, not reset: the solver tracks only position and orientation, so
                // a node's scale is whatever TransformApplySystem already wrote it to this frame.
                float existingScale = localTransformLookup[node].Scale;
                localTransformLookup[node] = new LocalTransform
                {
                    Position = localPosition,
                    Rotation = localRotation,
                    Scale = existingScale
                };
            }
        }
    }
}
