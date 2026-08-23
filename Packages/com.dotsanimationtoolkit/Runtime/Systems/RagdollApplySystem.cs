// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Writes every active ragdoll's solved pose back onto its nodes' <see cref="LocalTransform"/>
    /// (Phase D, amendment A50, spec §7.3) — the system that makes the physics actually visible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Runs every frame, for every enabled actor, whether it is awake or asleep — this is
    /// spec §9's G1, and it is the reason this system exists as its own step rather than being
    /// folded into <see cref="RagdollSolveSystem"/>.</strong> <c>TransformApplySystem</c> stomps
    /// <em>every</em> visible part's <c>LocalTransform</c> unconditionally, every single frame
    /// (that system's own remarks), and it runs earlier in the same presentation group, before this
    /// group even starts. A part that happens to also be a ragdoll body therefore gets its animated
    /// pose written by <c>TransformApplySystem</c> and then <em>overwritten again</em> by this system
    /// a few systems later in the same frame — every frame, without exception, including every frame
    /// a sleeping ragdoll's <see cref="RagdollBody.state"/> has not changed at all since the last one
    /// that moved it. Skip this system's write on a sleeping frame — the tempting "nothing changed,
    /// nothing to do" optimisation — and the very next frame, <c>TransformApplySystem</c> re-asserts
    /// the animated pose with nothing after it to correct it, and a ragdoll that had visibly settled
    /// on the ground snaps back into its animated stance. This is documented as the single most
    /// likely source of "the ragdoll works for one frame and then reverts" (spec §9 G1; the host
    /// game's own <c>Ragdoll2DSystem</c> carries the identical comment for the identical reason), so
    /// <see cref="RagdollSolveSystem"/> is written to return early on a sleeping actor without
    /// touching a single body's state, and this system is written with no early return on sleep at
    /// all — the two are a matched pair, not independent choices.
    /// </para>
    /// <para>
    /// <strong>The tempting alternative fix is wrong, and is not taken here.</strong> Adding
    /// <c>[WithDisabled(typeof(RagdollActor))]</c> to <c>TransformSampleSystem</c> (or
    /// <c>TransformApplySystem</c>) to make the animation "get out of the way" of a ragdolling part
    /// would exclude every actor that has never opted into ragdolling at all: <see cref="RagdollActor"/>
    /// is opt-in and, for such an actor, simply absent — and a <c>WithDisabled</c> filter on a
    /// component that is not present on the entity matches nothing, not "not disabled." That is the
    /// <c>PartFacing</c> trap this package has already paid for once (spec §9 G1's own text), and the
    /// sampler is correctly left completely untouched by this phase. The write-order fix above is the
    /// only correct one.
    /// </para>
    /// <para>
    /// <strong>Converting through the parent chain (spec §7.3).</strong> A body's solved
    /// <see cref="RagdollBodyState.position"/>/<see cref="RagdollBodyState.orientation"/> are the
    /// collision box's own center of mass and orientation in world space
    /// (<see cref="RagdollBodyParams"/>'s remarks), not the node's local values. This system inverts
    /// <see cref="RagdollBodyParams.boxCenter"/> to recover the node's world position, then converts
    /// world back to local against the node's <em>parent's own current world transform</em> —
    /// <see cref="RagdollTransformUtil.ComputeParentWorldTransform"/>, the exact inverse of the
    /// composition <see cref="RagdollCaptureSystem"/> used to seed world state in the first place.
    /// </para>
    /// <para>
    /// <strong>Bodies are written shallowest-first, and a body's parent may itself be a body already
    /// written earlier in this same pass.</strong> <see cref="RagdollBody"/>'s own ordering guarantee
    /// (its remarks) is what makes this correct: by the time this loop reaches a child body, the
    /// parent body's node — if it is a body — already holds this frame's freshly written
    /// <c>LocalTransform</c>, so walking the live parent chain picks up the parent's <em>solved</em>
    /// pose rather than whatever it held before this system ran. Reorder the buffer and a child's
    /// world-to-local conversion would be built against a stale parent frame.
    /// </para>
    /// <para>
    /// <strong>Only <c>LocalTransform</c> is written; <c>PostTransformMatrix</c> is left
    /// alone.</strong> Spec §7.3 says "and <c>PostTransformMatrix</c> where scale is involved," but
    /// nothing in <see cref="RagdollSolver"/> produces a scale for a body to begin with — the solver
    /// tracks only position and orientation (§6). A node's scale is therefore whatever it already
    /// was this frame (its rest scale, or whatever <c>TransformApplySystem</c> wrote from a sampled
    /// scale curve before this group ran), and this system's own <c>LocalTransform</c> write
    /// preserves the node's current <c>Scale</c> component unchanged rather than resetting it. The
    /// one place scale genuinely needs restoring is <see cref="RagdollReleaseSystem"/>, which copies
    /// <see cref="RagdollRestPose.postTransformMatrix"/> straight back — that is almost certainly what
    /// the spec phrase refers to, and this system does not duplicate that write.
    /// </para>
    /// <para>
    /// <strong>Known limitation, carried from <see cref="RagdollTransformUtil"/>.</strong> The
    /// parent-chain walk does not fold an ancestor's own <c>PostTransformMatrix</c> into the
    /// composition, matching the same simplification <c>BillboardResolveSystem</c>'s walk already
    /// makes. See that type's remarks.
    /// </para>
    /// </remarks>
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

    /// <summary>Writes one actor's solved bodies back onto their nodes.</summary>
    /// <remarks>
    /// <strong>Parallel safety.</strong> Each actor's <see cref="RagdollBody"/> buffer names only
    /// nodes belonging to that same actor's own hierarchy (spec §5.2: "a body's parent is its
    /// nearest ragdolled ancestor" — always within the same prefab), so different actors' Execute
    /// calls write disjoint sets of node entities through <see cref="localTransformLookup"/> even
    /// though the safety system cannot prove that statically, hence the restriction disable — the
    /// identical pattern <see cref="RagdollSolveSystem"/>'s own remarks contrast this system against.
    /// </remarks>
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

                // Invert RagdollCaptureSystem's boxCenter fold-in to recover the node's own world
                // pose from the body's (RagdollBodyParams' remarks).
                float3 nodeWorldPosition =
                    body.state.position - math.mul(body.state.orientation, body.parameters.boxCenter);
                quaternion nodeWorldRotation = body.state.orientation;

                RagdollTransformUtil.ComputeParentWorldTransform(
                    in node, in localTransformLookup, in parentLookup, out LocalTransform parentWorldTransform);

                float parentScale = parentWorldTransform.Scale;
                float3 localPosition = math.abs(parentScale) > 1e-8f
                    ? math.mul(math.inverse(parentWorldTransform.Rotation), nodeWorldPosition - parentWorldTransform.Position)
                        / parentScale
                    : float3.zero;
                quaternion localRotation = math.mul(math.inverse(parentWorldTransform.Rotation), nodeWorldRotation);

                // Scale is left exactly as it was — see the system's own remarks on why this system
                // never touches it.
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
