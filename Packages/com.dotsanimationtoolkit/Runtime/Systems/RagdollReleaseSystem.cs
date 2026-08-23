// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Puts an actor back exactly where it stood before its ragdoll started (Phase D, amendment A50,
    /// spec §7.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This system is the whole of the feature's headline promise.</strong> "Turning the
    /// ragdoll off resets the character to before" is not a nicety — it is what makes the toolbar
    /// toggle safe to press while authoring, and what lets a game revive a corpse without the
    /// character keeping a memento of how it fell. Everything it needs was captured by
    /// <see cref="RagdollCaptureSystem"/> into <see cref="RagdollRestPose"/>; this simply copies it
    /// back.
    /// </para>
    /// <para>
    /// <strong>The rest pose is the captured pose, not the rig's rest pose.</strong> "Before" means
    /// before <em>this drop</em>, not at the rig's origin — a character knocked over mid-swing and
    /// revived belongs back in that swing, on the frame the animation would be on now. That is why
    /// <see cref="RagdollRestPose"/> is filled at switch-on rather than baked once.
    /// </para>
    /// <para>
    /// <strong><c>OrderLast</c> in <see cref="AnimationToolkitRagdollSystemGroup"/>.</strong> The
    /// restore has to be the last word on the pose within the group, otherwise
    /// <see cref="RagdollApplySystem"/> would write solved body positions over it in the same frame
    /// the ragdoll was switched off, and the character would spend one frame in the pose it was
    /// released from. One frame is enough to see.
    /// </para>
    /// <para>
    /// <strong>Restoring is a one-frame event, not a state.</strong> The
    /// <see cref="RagdollStateFlags.RestoreNeeded"/> flag is cleared as the restore happens, so this
    /// job matches nothing on every subsequent frame — a disabled ragdoll costs a query and no
    /// writes. <see cref="TransformSampleSystem"/> resumes owning the pose the very next frame with
    /// no handshake of any kind, because it never stopped writing in the first place (spec §9 G1).
    /// </para>
    /// </remarks>
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

    /// <summary>Restores one actor's pre-ragdoll pose, on the frame its ragdoll was switched off.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Parallel safety.</strong> Each actor exclusively owns the node entities named by its
    /// own <see cref="RagdollBody"/> buffer — a node belongs to exactly one actor — so different
    /// actors' <c>Execute</c> calls write disjoint data despite the lookups reaching outside the
    /// iterated entity. That is what
    /// <see cref="Unity.Collections.NativeDisableParallelForRestrictionAttribute"/> asserts here, and
    /// it is the same argument <see cref="RagdollApplySystem"/> makes.
    /// </para>
    /// <para>
    /// <strong><c>WithPresent</c> is load-bearing.</strong> This job wants the actors whose
    /// <see cref="RagdollActor"/> is <em>disabled</em>, which is exactly the set an enabled-filtered
    /// query excludes. Taking <c>EnabledRefRO&lt;RagdollActor&gt;</c> as a parameter enrols the
    /// component as an <c>All</c> match, so without <c>[WithPresent]</c> this job would silently run
    /// on nothing at all, every frame, with no error of any kind (spec §9 G2).
    /// </para>
    /// </remarks>
    [BurstCompile]
    [WithPresent(typeof(RagdollActor))]
    internal partial struct ReleaseRagdollJob : IJobEntity
    {
        /// <summary>Not <c>[ReadOnly]</c>: the restore writes every ragdolled node's transform back.</summary>
        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup;

        /// <summary>Not <c>[ReadOnly]</c>: scale travels in the matrix, so it is restored too.</summary>
        [NativeDisableParallelForRestriction] public ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;

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
