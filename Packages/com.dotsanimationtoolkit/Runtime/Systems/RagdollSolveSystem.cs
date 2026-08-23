// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Advances every active ragdoll by this frame's fixed substeps (Phase D, amendment A50, spec
    /// §7.2) — the caller that owns <see cref="RagdollState.substepAccumulator"/> and calls
    /// <see cref="RagdollSolver.Step"/> the fixed number of times this frame's elapsed time and
    /// <see cref="RagdollConfig.maxSubstepsPerFrame"/> allow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One <see cref="SolveRagdollJob"/> per actor root, <c>ScheduleParallel</c>.</strong>
    /// Each root exclusively owns its own bodies (spec §7.2) — nothing here reaches into another
    /// actor's data — so this parallelises across actors with nothing to coordinate, unlike
    /// <see cref="RagdollApplySystem"/>, which does reach into other entities (the body nodes) and
    /// needs <c>NativeDisableParallelForRestriction</c> for it.
    /// </para>
    /// <para>
    /// <strong>The billboard frame is re-resolved every step, not read from the cache
    /// <see cref="RagdollCaptureSystem"/> wrote.</strong> <see cref="RagdollState.frameRotation"/>'s
    /// own remarks: an orbiting camera must carry the plane with it, so this job repeats exactly the
    /// same <c>BillboardMember</c>/<c>TryGetFrame</c> lookup capture used to seed the first frame.
    /// <see cref="RagdollState.planeOrigin"/>, by contrast, is read back unchanged — it was captured
    /// once and never needs to move (that field's own remarks).
    /// </para>
    /// <para>
    /// <strong>§9 G1 — sleeping actors are skipped here, not in <see cref="RagdollApplySystem"/>.</strong>
    /// A sleeping actor's job returns immediately after refreshing the frame, leaving
    /// <see cref="RagdollBody.state"/> exactly as the last awake step left it. Nothing about that
    /// early return excludes the actor from <see cref="RagdollApplySystem"/>, which runs
    /// unconditionally afterward for every enabled actor whether it just skipped dynamics or not —
    /// see that system's own remarks for why re-writing a pose that has not changed is still
    /// necessary every single frame.
    /// </para>
    /// <para>
    /// <strong>Flat <c>NativeArray</c> round-trip, per actor, every step it runs.</strong>
    /// <see cref="RagdollSolver.Step"/> is written and tested against
    /// <see cref="RagdollBodyParams"/>/<see cref="RagdollBodyState"/> arrays with no ECS type in
    /// reach (spec §6, §6.0) — that is what lets the identical function run against the editor
    /// preview's plain arrays too. This job copies <see cref="RagdollBody"/>'s composed fields out to
    /// <c>Allocator.Temp</c> arrays, calls <see cref="RagdollSolver.Step"/> the resolved number of
    /// times, and copies the results back — the same shape <see cref="RagdollSolver.Step"/> itself
    /// uses internally for its own scratch arrays.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(AnimationToolkitRagdollSystemGroup))]
    [UpdateAfter(typeof(RagdollProbeFallbackSystem))]
    [BurstCompile]
    public partial struct RagdollSolveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RagdollBody>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SystemAPI.TryGetSingleton(out RagdollConfig config);

            SolveRagdollJob solveJob = new SolveRagdollJob
            {
                config = config,
                deltaTime = SystemAPI.Time.DeltaTime,
                billboardMemberLookup = SystemAPI.GetComponentLookup<BillboardMember>(true),
                billboardRootElementLookup = SystemAPI.GetBufferLookup<BillboardRootElement>(true)
            };
            state.Dependency = solveJob.ScheduleParallel(state.Dependency);
        }
    }

    /// <summary>Steps one actor's ragdoll by this frame's fixed substeps.</summary>
    [BurstCompile]
    [WithAll(typeof(RagdollActor))]
    internal partial struct SolveRagdollJob : IJobEntity
    {
        public RagdollConfig config;
        public float deltaTime;

        [ReadOnly] public ComponentLookup<BillboardMember> billboardMemberLookup;
        [ReadOnly] public BufferLookup<BillboardRootElement> billboardRootElementLookup;

        private void Execute(
            in RagdollRigConfig rigConfig,
            ref RagdollState ragdollState,
            ref DynamicBuffer<RagdollBody> bodyElements,
            in DynamicBuffer<RagdollWorldContact> worldContacts)
        {
            int bodyCount = bodyElements.Length;
            if (bodyCount == 0)
            {
                return;
            }

            quaternion frameRotation = quaternion.identity;
            if (rigConfig.space == RagdollSpace.Planar2D)
            {
                Entity rootNode = bodyElements[0].node;
                if (billboardMemberLookup.HasComponent(rootNode))
                {
                    BillboardMember member = billboardMemberLookup[rootNode];
                    BillboardQuery.TryGetFrame(in member, in billboardRootElementLookup, out frameRotation);
                }
            }
            ragdollState.frameRotation = frameRotation;
            RagdollSolver.ComputePlaneNormal(in frameRotation, out float3 planeNormal);
            ragdollState.planeNormal = planeNormal;

            // §9 G1: dynamics are skipped for a sleeping actor, but this return happens only after
            // the frame above was refreshed — a sleeping actor's cached frame must still track an
            // orbiting camera, even though nothing about its pose is about to change.
            if ((ragdollState.flags & RagdollStateFlags.Sleeping) != 0)
            {
                return;
            }

            float substepDeltaTime = math.max(rigConfig.substepDeltaTime, 1e-4f);
            int maxSubsteps = math.max(1, config.maxSubstepsPerFrame);

            // Clamped before computing how many steps to run, not after — an accumulator allowed to
            // grow past the cap would carry unbounded debt forward on every stalled frame instead of
            // simply dropping the overflow, which is exactly the "unbounded catch-up" maxSubstepsPerFrame
            // exists to prevent (RagdollConfig.maxSubstepsPerFrame's own remarks).
            float maxAccumulator = substepDeltaTime * maxSubsteps;
            ragdollState.substepAccumulator = math.min(ragdollState.substepAccumulator + deltaTime, maxAccumulator);

            int stepsToRun = (int)math.floor(ragdollState.substepAccumulator / substepDeltaTime);
            if (stepsToRun <= 0)
            {
                return;
            }

            NativeArray<RagdollBodyParams> bodyParamsArray = new NativeArray<RagdollBodyParams>(bodyCount, Allocator.Temp);
            NativeArray<RagdollBodyState> bodyStateArray = new NativeArray<RagdollBodyState>(bodyCount, Allocator.Temp);
            for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
            {
                RagdollBody body = bodyElements[bodyIndex];
                bodyParamsArray[bodyIndex] = body.parameters;
                bodyStateArray[bodyIndex] = body.state;
            }

            NativeArray<RagdollContact> contactArray = new NativeArray<RagdollContact>(worldContacts.Length, Allocator.Temp);
            for (int contactIndex = 0; contactIndex < worldContacts.Length; contactIndex++)
            {
                RagdollWorldContact contact = worldContacts[contactIndex];
                contactArray[contactIndex] = new RagdollContact
                {
                    bodyIndex = contact.bodyIndex,
                    point = contact.point,
                    normal = contact.normal,
                    distance = contact.distance,
                    referencePosition = contact.referencePosition,
                    restitution = contact.restitution,
                    friction = contact.friction
                };
            }

            RagdollSolverSettings settings = new RagdollSolverSettings
            {
                space = rigConfig.space,
                worldGravity = config.worldGravity,
                planeOrigin = ragdollState.planeOrigin,
                gravityScale = rigConfig.gravityScale,
                frameRotation = frameRotation,
                solverIterations = rigConfig.solverIterations,
                substepDeltaTime = substepDeltaTime,
                jointStiffness = rigConfig.jointStiffness,
                jointDamping = rigConfig.jointDamping,
                sleepLinearSpeed = config.sleepLinearSpeed,
                sleepAngularSpeed = config.sleepAngularSpeed
            };

            bool belowSleepThreshold = true;
            for (int step = 0; step < stepsToRun; step++)
            {
                RagdollSolver.Step(
                    in settings, in bodyParamsArray, ref bodyStateArray, in contactArray, out belowSleepThreshold);
                ragdollState.substepAccumulator -= substepDeltaTime;
            }

            for (int bodyIndex = 0; bodyIndex < bodyCount; bodyIndex++)
            {
                ref RagdollBody body = ref bodyElements.ElementAt(bodyIndex);
                body.state = bodyStateArray[bodyIndex];
            }

            bodyParamsArray.Dispose();
            bodyStateArray.Dispose();
            contactArray.Dispose();

            float substepTimeRun = stepsToRun * substepDeltaTime;
            if (belowSleepThreshold)
            {
                ragdollState.sleepTimer += substepTimeRun;
                if (ragdollState.sleepTimer >= config.sleepDelaySeconds)
                {
                    ragdollState.flags |= RagdollStateFlags.Sleeping;
                }
            }
            else
            {
                ragdollState.sleepTimer = 0f;
            }
        }
    }
}
