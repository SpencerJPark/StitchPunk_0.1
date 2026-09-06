// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace DotsAnimationToolkit.Physics
{
    /// <summary>
    /// Optional, real-geometry <see cref="RagdollWorldContact"/> provider: box-casts each body
    /// against the world's <see cref="CollisionWorld"/>. Disables
    /// <see cref="RagdollProbeFallbackSystem"/> in <see cref="OnCreate"/> the moment this system
    /// exists — two providers filling the same buffer would double every contact.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitRagdollSystemGroup))]
    [UpdateAfter(typeof(RagdollCaptureSystem))]
    // RagdollSolveSystem keeps its own UpdateAfter(RagdollProbeFallbackSystem) and says nothing
    // about this optional type (Runtime cannot name a type that lives only in this assembly); this
    // attribute supplies the other half of that ordering edge, which Entities' sorter accepts from either side.
    [UpdateBefore(typeof(RagdollSolveSystem))]
    [CreateAfter(typeof(RagdollProbeFallbackSystem))]
    [BurstCompile]
    public partial struct RagdollPhysicsProbeSystem : ISystem
    {
        private const float MinimumProbeDistance = 0.01f; // floor under RagdollConfig.contactProbeRadius, so a zero-radius project still probes a non-zero reach

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RagdollBody>();
            state.RequireForUpdate<PhysicsWorldSingleton>();

            // The whole double-fill guard: disables the always-present fallback so exactly one
            // provider fills the buffer. Done here rather than in the fallback itself, since Runtime
            // cannot reference a type that only exists when Physics is present; CreateAfter
            // guarantees RagdollProbeFallbackSystem's own OnCreate has already run before this call.
            state.WorldUnmanaged.GetExistingSystemState<RagdollProbeFallbackSystem>().Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Same "missing config is a sane zero default" reasoning RagdollProbeFallbackSystem
            // uses — a world that has not yet run ConfigBootstrapSystem this tick still gets a
            // provider, just one probing with a zeroed reach until the singleton exists.
            SystemAPI.TryGetSingleton(out RagdollConfig config);
            PhysicsWorldSingleton physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

            PhysicsProbeRagdollJob probeJob = new PhysicsProbeRagdollJob
            {
                collisionWorld = physicsWorldSingleton.CollisionWorld,
                worldGravity = config.worldGravity,
                probeDistance = math.max(config.contactProbeRadius, MinimumProbeDistance)
            };
            state.Dependency = probeJob.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    [WithAll(typeof(RagdollActor))]
    internal partial struct PhysicsProbeRagdollJob : IJobEntity
    {
        [ReadOnly]
        public CollisionWorld collisionWorld;

        public float3 worldGravity;
        public float probeDistance;

        private void Execute(in DynamicBuffer<RagdollBody> bodyElements, ref DynamicBuffer<RagdollWorldContact> worldContacts)
        {
            worldContacts.Clear();

            // A single cast per body along gravity (never per-body velocity or a multi-direction
            // sweep): catches the resting-on-ground case against real geometry, one query per body
            // per frame. Does not catch a body colliding sideways into a wall it isn't currently
            // falling toward — a deliberate scope cut, not an oversight.
            float3 castDirection = ResolveCastDirection(in worldGravity);

            for (int bodyIndex = 0; bodyIndex < bodyElements.Length; bodyIndex++)
            {
                RagdollBody body = bodyElements[bodyIndex];
                if (!body.parameters.CollidesWithWorld)
                {
                    continue;
                }

                quaternion boxWorldOrientation = math.mul(body.state.orientation, body.parameters.boxRotation);

                bool hitFound = collisionWorld.BoxCast(
                    body.state.position,
                    boxWorldOrientation,
                    body.parameters.boxHalfExtents,
                    castDirection,
                    probeDistance,
                    out ColliderCastHit hitInfo,
                    CollisionFilter.Default);

                if (!hitFound)
                {
                    continue;
                }

                worldContacts.Add(new RagdollWorldContact
                {
                    bodyIndex = bodyIndex,
                    point = hitInfo.Position,
                    normal = hitInfo.SurfaceNormal,
                    // ColliderCastHit.Fraction is a [0, 1] fraction of the query's own max distance,
                    // not an absolute distance (unlike DistanceHit.Fraction) — scale it back up. A
                    // box-cast can only report [0, probeDistance]: unlike a true collider-distance
                    // query it cannot express "already 0.3 units inside the floor" as negative, only
                    // "touching now" as zero — the solver's own live-penetration re-derivation still
                    // resolves the common case, but a body that spawns deep inside geometry this
                    // exact frame is pushed out gradually rather than in one step.
                    distance = hitInfo.Fraction * probeDistance,
                    // The position this distance was measured from, paired with it so the solver's
                    // live-penetration re-derivation stays consistent across every substep of the
                    // frame — see RagdollContact.referencePosition.
                    referencePosition = body.state.position,
                    restitution = body.parameters.restitution,
                    friction = body.parameters.friction
                });
            }
        }

        /// <summary>World gravity, normalized; world down if gravity is (near) zero, so a probe always has a direction to cast along.</summary>
        private static float3 ResolveCastDirection(in float3 worldGravity)
        {
            float gravityLengthSq = math.lengthsq(worldGravity);
            if (gravityLengthSq < 1e-8f)
            {
                return new float3(0f, -1f, 0f);
            }
            return worldGravity * math.rsqrt(gravityLengthSq);
        }
    }
}
