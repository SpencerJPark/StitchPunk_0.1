// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Runs after <c>EventEmissionSystem</c> in <see cref="AnimationToolkitLogicSystemGroup"/>:
    /// honours an at-play <see cref="ActorRagdollRequest"/> and scans this frame's
    /// <see cref="AnimEventOutput"/> for an at-event ragdoll trigger.
    /// </summary>
    [UpdateInGroup(typeof(AnimationToolkitLogicSystemGroup))]
    [UpdateAfter(typeof(EventEmissionSystem))]
    [BurstCompile]
    public partial struct ActorRagdollTriggerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ActorProfile>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ApplyRagdollTriggersJob applyJob = new ApplyRagdollTriggersJob
            {
                ragdollActorLookup = SystemAPI.GetComponentLookup<RagdollActor>()
            };
            state.Dependency = applyJob.ScheduleParallel(state.Dependency);
        }
    }

    // Both ActorRagdollRequest and AnimEventsPending are WithPresent, not plain EnabledRefRW/RO
    // parameters: either would enrol its component as an All-match filter, silently excluding every
    // actor whose flag is currently off — which for both of these is the common case.
    [BurstCompile]
    [WithPresent(typeof(ActorRagdollRequest), typeof(AnimEventsPending))]
    internal partial struct ApplyRagdollTriggersJob : IJobEntity
    {
        // RagdollActor is opt-in and absent on actors with no ragdoll bodies. An EnabledRefRW/RO
        // parameter would enrol it as an All-match filter, excluding every actor that lacks it; a
        // lookup plus HasComponent reads it without requiring it.
        [NativeDisableParallelForRestriction] public ComponentLookup<RagdollActor> ragdollActorLookup;

        private void Execute(
            Entity entity,
            in ActorProfile actorProfile,
            in DynamicBuffer<AnimEventOutput> events,
            EnabledRefRO<AnimEventsPending> animEventsPendingEnabled,
            ref ActorRagdollRequest request,
            EnabledRefRW<ActorRagdollRequest> actorRagdollRequestEnabled)
        {
            // Copied to a local first: actorProfile is an `in` parameter, and reaching through it to
            // BlobAssetReference's ref-returning Value is the kind of construct that compiles into a
            // defensive copy. The local removes the question entirely.
            BlobAssetReference<ActorProfileBlob> profileReference = actorProfile.Value;
            if (!profileReference.IsCreated)
            {
                return;
            }

            if (actorRagdollRequestEnabled.ValueRO)
            {
                ApplyTrigger(entity, request.trigger, ref ragdollActorLookup);
                actorRagdollRequestEnabled.ValueRW = false;
            }

            if (!animEventsPendingEnabled.ValueRO)
            {
                return;
            }

            ref ActorProfileBlob profileBlob = ref profileReference.Value;
            for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
            {
                AnimEventOutput animEvent = events[eventIndex];
                if (animEvent.animationKey == 0u)
                {
                    continue;
                }

                if (!ActorProfileApi.TryFindAnimation(ref profileBlob, animEvent.animationKey, out int animationIndex))
                {
                    continue;
                }

                ref ActorAnimationBlob entry = ref profileBlob.animations[animationIndex];
                if (entry.ragdollTrigger == RagdollTrigger.None || entry.ragdollAtEventKey != animEvent.eventKey)
                {
                    continue;
                }

                ApplyTrigger(entity, entry.ragdollTrigger, ref ragdollActorLookup);
            }
        }

        // An actor without RagdollActor is a profile naming a trigger its rig cannot honour;
        // silently skipped here, never logged.
        private static void ApplyTrigger(Entity entity, RagdollTrigger trigger, ref ComponentLookup<RagdollActor> ragdollActorLookup)
        {
            if (!ragdollActorLookup.HasComponent(entity))
            {
                return;
            }

            ragdollActorLookup.SetComponentEnabled(entity, trigger == RagdollTrigger.Start);
        }
    }
}
