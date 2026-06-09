// using Unity.Burst;
// using Unity.Collections;
// using Unity.Collections.LowLevel.Unsafe;
// using Unity.Entities;
// using Unity.Mathematics;
// using Unity.Transforms;
// using Random = Unity.Mathematics.Random;
//
// [BurstCompile]
// [UpdateInGroup(typeof(ActionExecutionSystemGroup))]
// public partial struct TalkActionSystem : ISystem
// {
//     private ComponentLookup<LocalTransform>      transformLookup;
//     private ComponentLookup<ConversationContext> conversationContextLookup;
//     private ComponentLookup<CurrentAction> currentActionLookup;
//
//     [BurstCompile]
//     public void OnCreate(ref SystemState state)
//     {
//         state.RequireForUpdate<GameSceneTag>();
//         state.RequireForUpdate<TalkAction>();
//         
//         transformLookup           = state.GetComponentLookup<LocalTransform>(true);
//         conversationContextLookup = state.GetComponentLookup<ConversationContext>(true);
//         currentActionLookup = state.GetComponentLookup<CurrentAction>(true);
//     }
//
//     [BurstCompile]
//     public void OnUpdate(ref SystemState state)
//     {
//         transformLookup.Update(ref state);
//         conversationContextLookup.Update(ref state);
//         currentActionLookup.Update(ref state);
//         
//         state.Dependency = new TalkJob
//         {
//             elapsedTime               = (float)SystemAPI.Time.ElapsedTime,
//             transformLookup           = transformLookup,
//             conversationContextLookup = conversationContextLookup,
//             currentActionLookup = currentActionLookup,
//         }.ScheduleParallel(state.Dependency);
//     }
// }
//
// [BurstCompile]
// [WithAll(typeof(TalkAction))]
// [WithDisabled(typeof(ActionRequest), typeof(Dead))]
// [WithPresent( typeof(ActionTimer), typeof(PathRequest), typeof(AnimationRequest))]
// partial struct TalkJob : IJobEntity
// {
//     private const float CONVO_RANGE      = 1.5f;
//     private const float DURATION_MIN     = 5f;
//     private const float DURATION_MAX     = 15f;
//     private const float REPATH_THRESHOLD = 0.5f;
//     private const float SOCIAL_COOLDOWN  = 60f;
//
//     public float elapsedTime;
//     [ReadOnly] public ComponentLookup<LocalTransform>      transformLookup;
//     [ReadOnly, NativeDisableContainerSafetyRestriction]
//     public ComponentLookup<ConversationContext> conversationContextLookup;
//     [ReadOnly] public ComponentLookup<CurrentAction> currentActionLookup;
//
//     void Execute(
//         Entity                                    entity,
//         [EntityIndexInQuery] int                  entityIndex,
//         in LocalTransform                         transform,
//         in Movement                               movement,
//         ref ConversationContext                   context,
//         ref PathRequest                           pathRequest,
//         ref ActionTimer                           actionTimer,
//         ref DynamicBuffer<SetAnimation>            setAnimations,
//         ref DynamicBuffer<MotivationChangeRequest> changeRequests,
//         ref DynamicBuffer<RecentInteraction>       recentInteractions,
//         EnabledRefRW<TalkAction>                  talkEnabled,
//         EnabledRefRW<ActionRequest>               actionRequestEnabled,
//         EnabledRefRW<ActionTimer>                 actionTimerEnabled,
//         EnabledRefRW<PathRequest>                 pathRequestEnabled,
//         EnabledRefRW<AnimationRequest>            animationRequestEnabled)
//     {
//         Entity partner = context.conversationPartner;
//
//         bool partnerLost = 
//             partner == Entity.Null
//             || !conversationContextLookup.TryGetComponent(partner, out ConversationContext partnerContext)
//             || partnerContext.conversationPartner != entity
//             || currentActionLookup[partner].targetEntity != entity;
//
//         // Talking state — ActionTimerSystem already decremented actionTimer.time this frame
//         if (actionTimerEnabled.ValueRO)
//         {
//             if (partnerLost || actionTimer.time <= 0f)
//             {
//                 if (!partnerLost)
//                 {
//                     changeRequests.Add(new MotivationChangeRequest
//                     {
//                         needType   = NeedType.Social,
//                         changeType = MotivationChangeType.Add,
//                         value      = 50f,
//                     });
//                 }
//                 if (partner != Entity.Null)
//                 {
//                     recentInteractions.Add(new RecentInteraction
//                     {
//                         entity          = partner,
//                         cooldownEndTime = elapsedTime + SOCIAL_COOLDOWN,
//                     });
//                 }
//                 Terminate(ref context, ref pathRequest, talkEnabled, actionRequestEnabled,
//                     actionTimerEnabled, pathRequestEnabled);
//             }
//             return;
//         }
//
//         if (partnerLost)
//         {
//             Terminate(ref context, ref pathRequest, talkEnabled, actionRequestEnabled,
//                 actionTimerEnabled, pathRequestEnabled);
//             return;
//         }
//
//         if (!transformLookup.TryGetComponent(partner, out LocalTransform partnerTransform))
//             return;
//
//         float distSq = math.distancesq(transform.Position, partnerTransform.Position);
//         bool  inRange = distSq <= CONVO_RANGE * CONVO_RANGE;
//
//         // Both units approach on first frame — prevents the inRange branch from short-circuiting
//         // before either has tried to walk.
//         if (!context.hasStartedApproach)
//         {
//             context.hasStartedApproach = true;
//             AIUtils.BeginPathRequest(
//                 ref pathRequest,
//                 pathRequestEnabled,
//                 partnerTransform.Position,
//                 CONVO_RANGE * 0.9f);
//             return;
//         }
//
//         if (inRange)
//         {
//             AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
//
//             Random rng = new Random((uint)(entityIndex + 1) * (uint)(elapsedTime * 1000f + 1f));
//             actionTimer.time           = DURATION_MIN + rng.NextFloat(0f, DURATION_MAX - DURATION_MIN);
//             actionTimerEnabled.ValueRW = true;
//
//             setAnimations.Add(new SetAnimation
//             {
//                 layer     = AnimationLayerType.Action,
//                 animation = AnimationType.Talking,
//                 speed     = 1f,
//                 looping   = true,
//             });
//             animationRequestEnabled.ValueRW = true;
//             return;
//         }
//
//         // Both units path toward each other; repath if stopped or partner moved.
//         bool stoppedMoving = !movement.isMoving;
//         bool partnerMoved  = math.distancesq(pathRequest.targetPosition, partnerTransform.Position)
//                              >= REPATH_THRESHOLD * REPATH_THRESHOLD;
//         if (stoppedMoving || partnerMoved)
//         {
//             AIUtils.BeginPathRequest(
//                 ref pathRequest,
//                 pathRequestEnabled,
//                 partnerTransform.Position,
//                 CONVO_RANGE * 0.9f);
//         }
//     }
//
//     private static void Terminate(
//         ref ConversationContext       context,
//         ref PathRequest               pathRequest,
//         EnabledRefRW<TalkAction>      talkEnabled,
//         EnabledRefRW<ActionRequest>   actionRequestEnabled,
//         EnabledRefRW<ActionTimer>     actionTimerEnabled,
//         EnabledRefRW<PathRequest>     pathRequestEnabled)
//     {
//         AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
//         actionTimerEnabled.ValueRW   = false;
//         talkEnabled.ValueRW          = false;
//         context.conversationPartner  = Entity.Null;
//         context.isResponder          = false;
//         context.hasStartedApproach   = false;
//         actionRequestEnabled.ValueRW = true;
//     }
// }
