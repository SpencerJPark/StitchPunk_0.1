// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// <c>OrderLast</c> in <see cref="AnimationToolkitRagdollSystemGroup"/>: puts an actor back
    /// exactly where it stood before its ragdoll started, from <see cref="RagdollRestPose"/>. Must
    /// be the last word on the pose within the group, or <c>RagdollApplySystem</c> would write
    /// solved body positions over it in the same frame the ragdoll was switched off.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitRagdollSystemGroup), OrderLast = true)]
    [BurstCompile]
    public partial struct RagdollReleaseSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RagdollBody>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ReleaseRagdollJob releaseJob = new ReleaseRagdollJob
            {
                localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(),
                postTransformMatrixLookup = SystemAPI.GetComponentLookup<PostTransformMatrix>()
            };
            state.Dependency = releaseJob.ScheduleParallel(state.Dependency);
        }
    }

    // WithPresent is load-bearing: this job wants actors whose RagdollActor is disabled, and a
    // naked EnabledRefRO<RagdollActor> parameter would enrol it as an All match, so without
    // WithPresent this job would silently run on nothing at all, every frame, with no error.
    [BurstCompile]
    [WithPresent(typeof(RagdollActor))]
    internal partial struct ReleaseRagdollJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup; // not ReadOnly: the restore writes every ragdolled node's transform back

        [NativeDisableParallelForRestriction] public ComponentLookup<PostTransformMatrix> postTransformMatrixLookup; // not ReadOnly: scale travels in the matrix, so it is restored too

        private void Execute(
            EnabledRefRO<RagdollActor> ragdollActorEnabled,
            ref RagdollState ragdollState,
            in DynamicBuffer<RagdollBody> bodyElements,
            in DynamicBuffer<RagdollRestPose> restPoseElements)
        {
            if (ragdollActorEnabled.ValueRO)
            {
                return;
            }
            if ((ragdollState.flags & RagdollStateFlags.RestoreNeeded) == 0)
            {
                return;
            }

            // Defensive rather than expected: both buffers are baked at the same full length and
            // nothing resizes either at run time. Taking the shorter of the two costs one comparison
            // and turns a hypothetical desync into a partial restore instead of an out-of-range throw
            // inside a Bursted parallel job, which is a far worse thing to debug.
            int restorableCount = bodyElements.Length < restPoseElements.Length
                ? bodyElements.Length
                : restPoseElements.Length;

            for (int bodyIndex = 0; bodyIndex < restorableCount; bodyIndex++)
            {
                Entity nodeEntity = bodyElements[bodyIndex].node;
                if (nodeEntity == Entity.Null)
                {
                    continue;
                }

                RagdollRestPose restPose = restPoseElements[bodyIndex];

                if (localTransformLookup.HasComponent(nodeEntity))
                {
                    localTransformLookup[nodeEntity] = restPose.localTransform;
                }

                // Checked rather than assumed: a node without a PostTransformMatrix is a node whose
                // scale was never non-uniform, and writing one onto it would add a component this
                // system has no business adding — and could not add from inside a job anyway.
                if (postTransformMatrixLookup.HasComponent(nodeEntity))
                {
                    postTransformMatrixLookup[nodeEntity] = restPose.postTransformMatrix;
                }
            }

            // Clear the restore and arm the next capture in one write. A ragdoll switched on again
            // later must re-capture from wherever the animation has since carried the actor, never
            // reuse this buffer — which is precisely what leaving CaptureNeeded unset would cause.
            ragdollState.flags &= ~RagdollStateFlags.RestoreNeeded;
            ragdollState.flags |= RagdollStateFlags.CaptureNeeded;
            ragdollState.flags &= ~RagdollStateFlags.Sleeping;
            ragdollState.substepAccumulator = 0f;
            ragdollState.sleepTimer = 0f;
        }
    }
}
