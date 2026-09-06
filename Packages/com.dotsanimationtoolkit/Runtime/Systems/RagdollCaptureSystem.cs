// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// <c>OrderFirst</c> in <see cref="AnimationToolkitRagdollSystemGroup"/>: seeds a ragdoll's
    /// first substep the moment its <see cref="RagdollActor"/> switches on — captures the pre-drop
    /// pose, seeds every body's world position/orientation, resolves and caches the billboard
    /// gravity frame, and consumes an optional <see cref="RagdollLaunch"/>. Every other system in
    /// the group reads <see cref="RagdollBody.state"/> or the cached frame, so both must hold this
    /// frame's freshly seeded values before <c>RagdollSolveSystem</c> predicts a substep from them.
    /// </summary>
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

    // WithPresent(RagdollActor), read explicitly via EnabledRefRO below, rather than a naked
    // EnabledRefRO<RagdollActor> parameter: that would silently enrol RagdollActor as an
    // enabled-filtered All component, which happens to read correctly here but would be the wrong
    // filter the moment this job's shape was copied for something that also needed disabled actors.
    [BurstCompile]
    [WithPresent(typeof(RagdollActor))]
    internal partial struct CaptureRagdollJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> localTransformLookup;
        [ReadOnly] public ComponentLookup<Parent> parentLookup;
        [ReadOnly] public ComponentLookup<PostTransformMatrix> postTransformMatrixLookup;
        [ReadOnly] public ComponentLookup<BillboardMember> billboardMemberLookup;
        [ReadOnly] public BufferLookup<BillboardRootElement> billboardRootElementLookup;

        // Not [ReadOnly]: capture both reads a pending launch and disables it once consumed.
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
                // Cannot happen through ActorBaker's opt-in-or-nothing rule, but a Burst-compiled
                // job has no assert to lean on, so this stays a defensive early-out rather than an
                // index into an empty buffer.
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
                // later is a direct write-back, no chain to walk.
                restPoseElements[bodyIndex] = new RagdollRestPose
                {
                    localTransform = localTransformLookup[node],
                    postTransformMatrix = postTransformMatrixLookup.HasComponent(node)
                        ? postTransformMatrixLookup[node]
                        : new PostTransformMatrix { Value = float4x4.identity }
                };

                RagdollTransformMath.ComputeWorldTransform(
                    in node, in localTransformLookup, in parentLookup, out LocalTransform nodeWorldTransform);

                // The solver's body frame is the box's own center of mass, not the node's origin —
                // boxCenter folds in here.
                RagdollBodyState seededState = default;
                seededState.orientation = nodeWorldTransform.Rotation;
                seededState.position = nodeWorldTransform.Position
                    + math.mul(nodeWorldTransform.Rotation, body.parameters.boxCenter);
                seededState.linearVelocity = float3.zero;
                seededState.angularVelocity = float3.zero;
                body.state = seededState;
            }

            // The gravity frame is resolved from the root body's node: RagdollBody's own
            // shallowest-first ordering guarantees index 0 has no ragdolled ancestor. Re-resolved
            // every step by RagdollSolveSystem thereafter. Only attempted in Planar2D — Spatial3D
            // reads world gravity directly and never needs a frame.
            quaternion frameRotation = quaternion.identity;
            if (rigConfig.space == RagdollSpace.Planar2D)
            {
                Entity rootNode = bodyElements[0].node;
                if (billboardMemberLookup.HasComponent(rootNode))
                {
                    BillboardMember member = billboardMemberLookup[rootNode];
                    // Failure (no root, or the root was removed since bake) falls back to world
                    // identity, so a ragdoll on a rig with no billboard root simulates in the world
                    // XY plane rather than refusing to run.
                    BillboardApi.TryGetFrame(in member, in billboardRootElementLookup, out frameRotation);
                }
            }
            ragdollState.frameRotation = frameRotation;
            RagdollSolver.ComputePlaneNormal(in frameRotation, out float3 planeNormal);
            ragdollState.planeNormal = planeNormal;

            // The actor root's own world position, captured once here and never revisited — unlike
            // frameRotation, this does not need to track anything after the drop.
            RagdollTransformMath.ComputeWorldTransform(
                in actorEntity, in localTransformLookup, in parentLookup, out LocalTransform actorWorldTransform);
            ragdollState.planeOrigin = actorWorldTransform.Position;

            ragdollState.substepAccumulator = 0f;
            ragdollState.sleepTimer = 0f;
            ragdollState.flags &= ~(RagdollStateFlags.CaptureNeeded | RagdollStateFlags.Sleeping);

            // Raised here rather than by whoever enables the ragdoll, because this is the moment a
            // rest pose actually exists to restore. Setting it any earlier would let
            // RagdollReleaseSystem write an uncaptured (zeroed) buffer over a perfectly good animated pose.
            ragdollState.flags |= RagdollStateFlags.RestoreNeeded;

            if (launchLookup.HasComponent(actorEntity))
            {
                // Lands on the root body only: RagdollLaunch carries no body index, and the root is
                // what every other body's joint/limit constraints anchor against, so a hit there
                // propagates through the chain over the following substeps.
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
