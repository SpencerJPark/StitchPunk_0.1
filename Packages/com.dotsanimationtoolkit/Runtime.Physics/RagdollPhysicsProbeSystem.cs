// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace DotsAnimationToolkit.Physics
{
    /// <summary>
    /// The optional, real-geometry <see cref="RagdollWorldContact"/> provider (Phase D, amendment
    /// A50, spec §7.5): box-casts each body against the world's <see cref="CollisionWorld"/> instead
    /// of the always-present <see cref="RagdollProbeFallbackSystem"/>'s single infinite ground plane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This assembly exists only to keep this one system out of <c>Runtime</c>.</strong> D2's
    /// decision (spec §7.5) is that the core package never names a physics assembly — the solver runs
    /// in the editor preview, where no ECS <c>PhysicsWorld</c> exists — so a Unity Physics dependency
    /// has to live somewhere a buyer can delete. <c>DotsAnimationToolkit.Runtime.Physics.asmdef</c>
    /// carries a <c>versionDefine</c> (<c>com.unity.physics</c> ≥ 1.0.0 →
    /// <c>DOTS_ANIM_TOOLKIT_PHYSICS</c>) and a matching <c>defineConstraints</c> entry, so without
    /// Unity Physics installed this whole assembly is excluded from compilation and its reference to
    /// <c>Unity.Physics</c> is never evaluated. Confirmed in the Editor before this file was written
    /// (D7's own build-step verification item, spec §7.5): a scratch asmdef with a permanently-false
    /// <c>defineConstraints</c> symbol and a reference to a nonexistent assembly produced <em>no</em>
    /// console output at all — not even a warning — for either the excluded assembly or its
    /// unresolvable reference.
    /// </para>
    /// <para>
    /// <strong>Disables <see cref="RagdollProbeFallbackSystem"/> the moment this system exists.</strong>
    /// Two providers filling the same <see cref="RagdollWorldContact"/> buffer would double every
    /// contact, so exactly one may run. The fallback system cannot be the one to check for this —
    /// it lives in <c>Runtime</c> and the whole point of this assembly split is that <c>Runtime</c>
    /// may not reference anything that only exists when Unity Physics is present (a
    /// <c>PhysicsWorldSingleton</c> presence check would be exactly such a reference). So the
    /// dependency runs the other way: <see cref="OnCreate"/> resolves the fallback's own
    /// <see cref="SystemState"/> through <see cref="WorldUnmanaged.GetExistingSystemState{T}"/> and
    /// sets <see cref="SystemState.Enabled"/> to <c>false</c> — the same "disable in <c>OnCreate</c>
    /// and never run again" shape <c>ConfigBootstrapSystem</c> already uses on itself. This is why
    /// this type carries <see cref="CreateAfterAttribute"/>: <c>GetExistingSystemState</c> throws if
    /// the target has not been created yet, and <c>CreateAfter</c> is what guarantees
    /// <see cref="RagdollProbeFallbackSystem"/>'s own <c>OnCreate</c> has already run — both systems
    /// sit in <see cref="AnimationToolkitRagdollSystemGroup"/>, which is the attribute's own
    /// requirement (they must share a group). A disabled system still participates in the group's
    /// ordering sort, so <see cref="RagdollProbeFallbackSystem"/>'s own <c>UpdateAfter</c>/
    /// <c>UpdateBefore</c> attributes — asserted by <c>RagdollProbeFallback_RunsAfterCapture_AndBeforeSolve</c>
    /// — are left completely untouched; this system only ever stops it from executing.
    /// </para>
    /// <para>
    /// <strong>Ordered against <see cref="RagdollSolveSystem"/>, not the other way around.</strong>
    /// <c>Runtime</c> cannot name a type that lives only in this optional assembly, so
    /// <see cref="RagdollSolveSystem"/> keeps its existing <c>UpdateAfter(RagdollProbeFallbackSystem)</c>
    /// and says nothing about this type. This system supplies the missing half of the edge itself —
    /// <see cref="UpdateBeforeAttribute"/> — which is enough: Entities' system sorter accepts an
    /// ordering constraint declared by either side of the pair.
    /// </para>
    /// <para>
    /// <strong>Cast direction is world gravity, not per-body velocity or a multi-direction sweep.</strong>
    /// A single box-cast per body, straight along <see cref="RagdollConfig.worldGravity"/> (falling
    /// back to world down if a host ever zeroes gravity out), catches the resting-on-ground case the
    /// fallback already handles — now against real geometry instead of one infinite plane — with one
    /// query per body per frame. It does <em>not</em> catch a body colliding sideways into a wall it
    /// is not currently falling toward; spec §7.5 asks for "box-casts per body" without specifying a
    /// direction scheme, and a full multi-direction sweep is a real scope increase this phase does not
    /// take. Documented here rather than silently, per this package's own convention for a scope cut.
    /// </para>
    /// <para>
    /// <strong>The reported <c>distance</c> can only reach zero, never go negative, on a body that is
    /// already deeply penetrating geometry at the moment this probe runs.</strong> A box-cast reports
    /// distance-to-first-contact along the cast ray, clamped to <c>[0, probeDistance]</c> — unlike a
    /// true collider-distance query, it cannot express "already 0.3 units inside the floor" as a
    /// negative number, only "touching now" as zero. <see cref="RagdollSolver.CorrectWorldContactPosition"/>
    /// still resolves the common case correctly, because it adds this step's own predicted motion
    /// along the contact normal to whatever the probe reported (spec §6.1's live re-derivation, the
    /// gotcha D4 found the hard way) — a body sitting exactly at the surface with gravity pulling it
    /// further in still measures negative and gets pushed back out. The case this under-corrects is a
    /// body spawned, teleported, or launched deep inside geometry in the same frame this probe reads
    /// it; the solver will push it out gradually rather than in one step. Not exercised by this
    /// package's own test suite (no test assembly references this optional one), and worth revisiting
    /// only if that specific symptom shows up in play-testing (§7.5 does not ask for it up front).
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitRagdollSystemGroup))]
    [UpdateAfter(typeof(RagdollCaptureSystem))]
    [UpdateBefore(typeof(RagdollSolveSystem))]
    [CreateAfter(typeof(RagdollProbeFallbackSystem))]
    [BurstCompile]
    public partial struct RagdollPhysicsProbeSystem : ISystem
    {
        /// <summary>
        /// Floor under <see cref="RagdollConfig.contactProbeRadius"/> for the cast's max distance, so
        /// a project that leaves the radius at zero still probes a small, non-zero reach rather than
        /// a cast that can never hit anything.
        /// </summary>
        private const float MinimumProbeDistance = 0.01f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RagdollBody>();
            state.RequireForUpdate<PhysicsWorldSingleton>();

            // See the type remarks: this is the whole double-fill guard. Runs once, at world
            // creation, well before either provider's first OnUpdate.
            state.WorldUnmanaged.GetExistingSystemState<RagdollProbeFallbackSystem>().Enabled = false;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Same "missing config is a sane zero default" reasoning RagdollProbeFallbackSystem
            // documents for itself — a world that has not yet run ConfigBootstrapSystem this tick
            // still gets a provider, just one probing with a zeroed reach until the singleton exists.
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

    /// <summary>
    /// Rebuilds one actor's world-contact buffer from real <see cref="CollisionWorld"/> box-casts.
    /// </summary>
    /// <remarks>
    /// Every write lands in the buffer of the entity being iterated — the same disjoint-per-actor
    /// shape <see cref="FallbackProbeRagdollJob"/> uses, so parallel actors touch nothing shared.
    /// </remarks>
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
                    // not an absolute distance (unlike DistanceHit.Fraction) — scale it back up.
                    distance = hitInfo.Fraction * probeDistance,
                    // The position this distance was measured from. Paired with the distance above
                    // so the solver's live-penetration re-derivation stays consistent across every
                    // substep of the frame — see RagdollContact.referencePosition.
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
