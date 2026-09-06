// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs between <c>RagdollCaptureSystem</c> and <c>RagdollSolveSystem</c>: the always-present
    /// <see cref="RagdollWorldContact"/> provider, one horizontal ground plane at
    /// <see cref="RagdollConfig.fallbackGroundHeight"/>. This is the seam that keeps Unity Physics
    /// optional — an optional physics assembly's own probe system is responsible for disabling this
    /// one when it is present; <c>RagdollSolveSystem</c> reads the buffer and names neither provider.
    /// </summary>
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
            // A world that has not yet run ConfigBootstrapSystem this tick (an earlier group of the
            // same frame) falls back to the zero-valued default — ground at y = 0, no probe
            // inflation — rather than skipping the provider outright.
            SystemAPI.TryGetSingleton(out RagdollConfig config);

            FallbackProbeRagdollJob probeJob = new FallbackProbeRagdollJob
            {
                fallbackGroundHeight = config.fallbackGroundHeight,
                contactProbeRadius = config.contactProbeRadius
            };
            state.Dependency = probeJob.ScheduleParallel(state.Dependency);
        }
    }

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
                // The reported distance accounts for the box's own extent, not just its center:
                // RagdollSolver.CorrectWorldContactPosition subtracts distance straight from the
                // body's center position, so a raw center-to-plane gap would let every body sink in
                // by its own half-height before the solver saw a penetration at all.
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
