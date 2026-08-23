// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Seeds a ragdoll's first substep the moment its <see cref="RagdollActor"/> switches on (Phase D,
    /// amendment A50, spec §7.1): captures the pre-drop pose into <see cref="RagdollRestPose"/>,
    /// seeds every body's world position and orientation from the live hierarchy, resolves and caches
    /// the billboard gravity frame, and consumes an optional <see cref="RagdollLaunch"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong><c>OrderFirst</c> in <see cref="AnimationToolkitRagdollSystemGroup"/>.</strong> Every
    /// other system in the group reads <see cref="RagdollBody.state"/> or
    /// <see cref="RagdollState"/>'s cached frame; both must hold this frame's freshly seeded values
    /// before <see cref="RagdollSolveSystem"/> ever predicts a substep from them.
    /// </para>
    /// <para>
    /// <strong>Structural-free.</strong> Both <see cref="RagdollBody"/> and
    /// <see cref="RagdollRestPose"/> are baked at full length (their own remarks), so this is a write
    /// into existing buffer slots every time, never an <c>AddBuffer</c>/resize.
    /// </para>
    /// <para>
    /// <strong>§9 G2/G3.</strong> <see cref="CaptureRagdollJob"/> takes
    /// <c>EnabledRefRO&lt;RagdollActor&gt;</c> under <c>[WithPresent(typeof(RagdollActor))]</c>,
    /// exactly as <c>CommandApplySystem</c> does for <c>BoundsDirty</c> — a naked
    /// <c>EnabledRefRO&lt;RagdollActor&gt;</c> parameter would silently enrol <c>RagdollActor</c> as
    /// an enabled-filtered <c>All</c> query component, which happens to read correctly here (capture
    /// only ever wants <em>enabled</em> actors) but would be the wrong filter the moment this job's
    /// shape was copied for anything that also needed to see disabled ones — <c>WithPresent</c> plus
    /// an explicit <c>ValueRO</c> check makes the condition a statement in the body instead of an
    /// implicit property of the attribute list.
    /// </para>
    /// <para>
    /// <strong>The root body's node is <see cref="RagdollBody"/> index 0.</strong> The buffer's own
    /// shallowest-first ordering guarantee (its remarks) means the first element is always a body
    /// with no ragdolled ancestor — the frame source spec §6.2 calls "the root body's node" — even in
    /// the disconnected-ragdoll case where a second root exists later in the buffer (spec §4.1's V-R6
    /// warning). Only the first root's frame is used; a disconnected rig's second articulation falls
    /// in whichever frame the first one resolved, which is the same "one warning, not a per-root
    /// concept" reading <c>ActorBaker.WarnIfDisconnected</c> already takes.
    /// </para>
    /// <para>
    /// <strong>The launch impulse lands on the root body only.</strong> <see cref="RagdollLaunch"/>
    /// carries no body index (spec §5.5), and the root body is the one every other body's joint and
    /// limit constraints ultimately anchor against, so a hit there propagates through the chain over
    /// the following substeps the same way a real blow would. Read through a
    /// <c>ComponentLookup</c> with <c>HasComponent</c>, never as a job parameter — <see cref="RagdollLaunch"/>'s
    /// own remarks name this the <c>PartFacing</c> trap, already paid for once.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitRagdollSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct RagdollCaptureSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RagdollBody>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            CaptureRagdollJob captureJob = new CaptureRagdollJob
            {
                localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                parentLookup = SystemAPI.GetComponentLookup<Parent>(true),
                postTransformMatrixLookup = SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
                billboardMemberLookup = SystemAPI.GetComponentLookup<BillboardMember>(true),
                billboardRootElementLookup = SystemAPI.GetBufferLookup<BillboardRootElement>(true),
                launchLookup = SystemAPI.GetComponentLookup<RagdollLaunch>()
            };
            state.Dependency = captureJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>Captures one actor's ragdoll drop, when it has just been asked to start one.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Parallel safety.</strong> Every write this job makes — the actor's own buffers and
    /// component, plus its own entry in <see cref="launchLookup"/> — lands on the entity being
    /// iterated, never on another actor's, so different actors' Execute calls touch disjoint data.
    /// The read-only lookups (<see cref="localTransformLookup"/>, <see cref="parentLookup"/>,
    /// <see cref="postTransformMatrixLookup"/>, <see cref="billboardMemberLookup"/>,
    /// <see cref="billboardRootElementLookup"/>) walk into other entities' data — a node's ancestors,
    /// a billboard root's buffer — but reads need no restriction regardless of aliasing.
    /// </para>
    /// </remarks>
    [BurstCompile]
    [WithPresent(typeof(RagdollActor))]
    internal partial struct CaptureRagdollJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> localTransformLookup;
        [ReadOnly] public ComponentLookup<Parent> parentLookup;
        [ReadOnly] public ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;
        [ReadOnly] public ComponentLookup<BillboardMember> billboardMemberLookup;
        [ReadOnly] public BufferLookup<BillboardRootElement> billboardRootElementLookup;

        /// <summary>
        /// Not <c>[ReadOnly]</c>: capture both reads a pending launch and disables it once consumed.
        /// See <see cref="RagdollLaunch"/>'s remarks for why this is a lookup and never a job
        /// parameter.
        /// </summary>
        [NativeDisableParallelForRestriction] public ComponentLookup<RagdollLaunch> launchLookup;

        private void Execute(
            Entity actorEntity,
            EnabledRefRO<RagdollActor> ragdollActorEnabled,
            in RagdollRigConfig rigConfig,
            ref RagdollState ragdollState,
            ref DynamicBuffer<RagdollBody> bodyElements,
            ref DynamicBuffer<RagdollRestPose> restPoseElements)
        {
            if (!ragdollActorEnabled.ValueRO)
            {
                return;
            }
            if ((ragdollState.flags & RagdollStateFlags.CaptureNeeded) == 0)
            {
                return;
            }
            if (bodyElements.Length == 0)
            {
                // Cannot happen through ActorBaker (spec §7.1's opt-in-or-nothing rule), but a
                // Burst-compiled job has no assert to lean on, so this stays a defensive early-out
                // rather than an index into an empty buffer.
                return;
            }

            for (int bodyIndex = 0; bodyIndex < bodyElements.Length; bodyIndex++)
            {
                ref RagdollBody body = ref bodyElements.ElementAt(bodyIndex);
                Entity node = body.node;
                if (!localTransformLookup.HasComponent(node))
                {
                    continue;
                }

                // The rest pose is the node's own local values, not a world quantity — restoring it
                // later is a direct write-back, no chain to walk (RagdollRestPose's own remarks).
                restPoseElements[bodyIndex] = new RagdollRestPose
                {
                    localTransform = localTransformLookup[node],
                    postTransformMatrix = postTransformMatrixLookup.HasComponent(node)
                        ? postTransformMatrixLookup[node]
                        : new PostTransformMatrix { Value = float4x4.identity }
                };

                RagdollTransformUtil.ComputeWorldTransform(
                    in node, in localTransformLookup, in parentLookup, out LocalTransform nodeWorldTransform);

                // The solver's body frame is the box's own center of mass, not the node's origin
                // (RagdollBodyParams' remarks) — boxCenter folds in here exactly as that type
                // specifies capture must.
                RagdollBodyState seededState = default;
                seededState.orientation = nodeWorldTransform.Rotation;
                seededState.position = nodeWorldTransform.Position
                    + math.mul(nodeWorldTransform.Rotation, body.parameters.boxCenter);
                seededState.linearVelocity = float3.zero;
                seededState.angularVelocity = float3.zero;
                body.state = seededState;
            }

            // The gravity frame (§6.2): resolved from the root body's node (index 0 — see the
            // system's remarks), re-resolved every step by RagdollSolveSystem thereafter. Only
            // attempted in Planar2D — a Spatial3D rig reads world gravity directly and never needs a
            // frame (spec §6.3), so this skips the BillboardMember lookup entirely for that case.
            quaternion frameRotation = quaternion.identity;
            if (rigConfig.space == RagdollSpace.Planar2D)
            {
                Entity rootNode = bodyElements[0].node;
                if (billboardMemberLookup.HasComponent(rootNode))
                {
                    BillboardMember member = billboardMemberLookup[rootNode];
                    // Failure (no root, or the root was removed since bake) falls back to world
                    // identity, exactly as BillboardQuery.TryGetFrame documents — not silently: the
                    // fallback is the same "unmodified orientation" reading BillboardRootElement
                    // itself already uses for a failed resolve, so a ragdoll on a rig with no
                    // billboard root simulates in the world XY plane rather than refusing to run.
                    BillboardQuery.TryGetFrame(in member, in billboardRootElementLookup, out frameRotation);
                }
            }
            ragdollState.frameRotation = frameRotation;
            RagdollSolver.ComputePlaneNormal(in frameRotation, out float3 planeNormal);
            ragdollState.planeNormal = planeNormal;

            // planeOrigin (§6.2): the actor root's own world position, captured once here and never
            // revisited — see RagdollState.planeOrigin's remarks for why it does not need to track
            // the actor the way frameRotation tracks the camera.
            RagdollTransformUtil.ComputeWorldTransform(
                in actorEntity, in localTransformLookup, in parentLookup, out LocalTransform actorWorldTransform);
            ragdollState.planeOrigin = actorWorldTransform.Position;

            ragdollState.substepAccumulator = 0f;
            ragdollState.sleepTimer = 0f;
            ragdollState.flags &= ~(RagdollStateFlags.CaptureNeeded | RagdollStateFlags.Sleeping);

            // Raised here rather than by whoever enables the ragdoll, because this is the moment a
            // rest pose actually exists to restore. Setting it any earlier would let
            // RagdollReleaseSystem write an uncaptured buffer over a perfectly good animated pose —
            // and the buffer is baked at full length, so it is zeroed rather than obviously absent.
            ragdollState.flags |= RagdollStateFlags.RestoreNeeded;

            if (launchLookup.HasComponent(actorEntity))
            {
                RagdollLaunch launch = launchLookup[actorEntity];
                ref RagdollBody rootBody = ref bodyElements.ElementAt(0);
                RagdollBodyState rootState = rootBody.state;
                RagdollSolver.ApplyLaunchImpulse(
                    in rootBody.parameters, ref rootState,
                    in launch.worldImpulse, in launch.worldPoint, in launch.worldTorque);
                rootBody.state = rootState;
                launchLookup.SetComponentEnabled(actorEntity, false);
            }
        }
    }
}
