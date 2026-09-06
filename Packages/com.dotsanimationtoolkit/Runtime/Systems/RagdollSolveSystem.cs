// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>RagdollProbeFallbackSystem</c>: advances every active ragdoll by this frame's
    /// fixed substeps, owning <see cref="RagdollState.substepAccumulator"/> and calling
    /// <see cref="RagdollSolver.Step"/> as many times as the elapsed time and
    /// <see cref="RagdollConfig.maxSubstepsPerFrame"/> allow. A sleeping actor's dynamics are
    /// skipped here, never in <c>RagdollApplySystem</c>, which still writes its unchanged pose
    /// unconditionally every frame afterward.
    /// </summary>
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

            // Re-resolved every step, not read from RagdollCaptureSystem's cache: an orbiting camera
            // must carry the plane with it.
            quaternion frameRotation = quaternion.identity;
            if (rigConfig.space == RagdollSpace.Planar2D)
            {
                Entity rootNode = bodyElements[0].node;
                if (billboardMemberLookup.HasComponent(rootNode))
                {
                    BillboardMember member = billboardMemberLookup[rootNode];
                    BillboardApi.TryGetFrame(in member, in billboardRootElementLookup, out frameRotation);
                }
            }
            ragdollState.frameRotation = frameRotation;
            RagdollSolver.ComputePlaneNormal(in frameRotation, out float3 planeNormal);
            ragdollState.planeNormal = planeNormal;

            // Dynamics are skipped for a sleeping actor, but only after the frame above was
            // refreshed — a sleeping actor's cached frame must still track an orbiting camera even
            // though nothing about its pose is about to change.
            if ((ragdollState.flags & RagdollStateFlags.Sleeping) != 0)
            {
                return;
            }

            float substepDeltaTime = math.max(rigConfig.substepDeltaTime, 1e-4f);
            int maxSubsteps = math.max(1, config.maxSubstepsPerFrame);

            // Clamped before computing how many steps to run, not after — an accumulator allowed to
            // grow past the cap would carry unbounded debt forward on every stalled frame instead of
            // simply dropping the overflow.
            float maxAccumulator = substepDeltaTime * maxSubsteps;
            ragdollState.substepAccumulator = math.min(ragdollState.substepAccumulator + deltaTime, maxAccumulator);

            int stepsToRun = (int)math.floor(ragdollState.substepAccumulator / substepDeltaTime);
            if (stepsToRun <= 0)
            {
                return;
            }

            // RagdollSolver.Step is written against plain arrays with no ECS type in reach, so the
            // same function also runs the editor preview; this round-trips through Temp arrays to call it.
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
