// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The always-present <see cref="RagdollWorldContact"/> provider (Phase D, amendment A50, spec
    /// §7.5): one horizontal ground plane at <see cref="RagdollConfig.fallbackGroundHeight"/>, which
    /// is what a ragdoll falls onto with no other package present.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The seam that keeps Unity Physics optional (spec §5.6, §7.5).</strong> This system and
    /// the eventual <c>Runtime.Physics</c> assembly's <c>RagdollPhysicsProbeSystem</c> (D7) are two
    /// independent fillers of the same buffer; <see cref="RagdollSolveSystem"/> reads
    /// <see cref="RagdollWorldContact"/> and names neither. D7's job is to disable this one when the
    /// optional assembly is present — this phase does not do that, so both would double-fill the
    /// buffer if D7 shipped without touching this system, which is D7's obligation to avoid, not
    /// this one's to anticipate.
    /// </para>
    /// <para>
    /// <strong>Every contact's <c>bodyIndex</c> is populated</strong> (spec §5.6's own emphasis) — a
    /// contact this provider cannot attribute to a body is unusable by
    /// <see cref="RagdollSolveSystem"/>, which resolves contacts per body, not per actor.
    /// </para>
    /// <para>
    /// <strong>The reported <c>distance</c> accounts for the box's own extent, not just its
    /// center.</strong> <see cref="RagdollSolver.CorrectWorldContactPosition"/> subtracts
    /// <c>distance</c> straight from a body's center position, so a provider that reported the raw
    /// center-to-plane gap would let every body sink in by its own half-height before the solver
    /// registered a penetration at all. <see cref="RagdollSolver.ComputeBoxProjectedRadius"/> — a D4
    /// addition to the solver for exactly this caller — supplies the box's true projected half-width
    /// along the ground normal.
    /// </para>
    /// <para>
    /// <strong>A body with <see cref="RagdollBodyParams.CollidesWithWorld"/> off gets no contact at
    /// all</strong>, rather than one the solver would skip anyway (<see cref="RagdollSolver.Step"/>
    /// already checks the flag before consuming a contact) — smaller buffers for the common case of a
    /// rig where only some bodies (a cape tip, spec §3.3's example) opt out.
    /// </para>
    /// <para>
    /// <strong>Restitution and friction come from the struck body, not the ground.</strong> Neither
    /// spec §5.7's <see cref="RagdollConfig"/> nor §3.2's rig settings author a ground material, so
    /// there is nothing else to report; <see cref="RagdollSolver"/>'s own contact response already
    /// combines a contact's values with the body's (max restitution, geometric-mean friction), which
    /// degenerates cleanly to "the body's own material" when both sides report the same numbers.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitRagdollSystemGroup))]
    [UpdateAfter(typeof(RagdollCaptureSystem))]
    [UpdateBefore(typeof(RagdollSolveSystem))]
    [BurstCompile]
    public partial struct RagdollProbeFallbackSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RagdollBody>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // A world that has not yet run ConfigBootstrapSystem this tick (it lives in a different,
            // earlier group of the same frame) falls back to the zero-valued default — ground at
            // y = 0, no probe inflation — rather than skipping the provider outright, matching
            // TransformSampleSystem's own "missing config is a sane default, not a reason to do
            // nothing" precedent.
            SystemAPI.TryGetSingleton(out RagdollConfig config);

            FallbackProbeRagdollJob probeJob = new FallbackProbeRagdollJob
            {
                fallbackGroundHeight = config.fallbackGroundHeight,
                contactProbeRadius = config.contactProbeRadius
            };
            state.Dependency = probeJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>Rebuilds one actor's world-contact buffer against the fallback ground plane.</summary>
    /// <remarks>
    /// Every write lands in the buffer of the entity being iterated, so parallel actors touch
    /// disjoint data with nothing to coordinate.
    /// </remarks>
    [BurstCompile]
    [WithAll(typeof(RagdollActor))]
    internal partial struct FallbackProbeRagdollJob : IJobEntity
    {
        public float fallbackGroundHeight;
        public float contactProbeRadius;

        private void Execute(in DynamicBuffer<RagdollBody> bodyElements, ref DynamicBuffer<RagdollWorldContact> worldContacts)
        {
            worldContacts.Clear();

            float3 groundNormal = new float3(0f, 1f, 0f);
            for (int bodyIndex = 0; bodyIndex < bodyElements.Length; bodyIndex++)
            {
                RagdollBody body = bodyElements[bodyIndex];
                if (!body.parameters.CollidesWithWorld)
                {
                    continue;
                }

                quaternion boxWorldOrientation = math.mul(body.state.orientation, body.parameters.boxRotation);
                RagdollSolver.ComputeBoxProjectedRadius(
                    in body.parameters.boxHalfExtents, in boxWorldOrientation, in groundNormal, out float projectedRadius);

                float distance = body.state.position.y - fallbackGroundHeight - projectedRadius - contactProbeRadius;

                worldContacts.Add(new RagdollWorldContact
                {
                    bodyIndex = bodyIndex,
                    point = new float3(body.state.position.x, fallbackGroundHeight, body.state.position.z),
                    normal = groundNormal,
                    distance = distance,
                    referencePosition = body.state.position,
                    restitution = body.parameters.restitution,
                    friction = body.parameters.friction
                });
            }
        }
    }
}
