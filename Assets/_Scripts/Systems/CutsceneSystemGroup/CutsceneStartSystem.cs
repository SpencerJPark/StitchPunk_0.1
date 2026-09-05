using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Consumes every <see cref="CutsceneRequest"/> signal entity: finds the baked
/// <c>CutsceneStage</c>, starts the toolkit's playback request, applies binding overrides, and
/// gates every bound actor off AI/movement (see RULES.md's contract-component convention).
/// Not [BurstCompile] — CutscenePlaybackApi is a managed EntityManager API and this system makes
/// structural changes (CreatePlayRequestFromStage adds components).
/// </summary>
[UpdateInGroup(typeof(CutsceneSystemGroup), OrderFirst = true)]
public partial struct CutsceneStartSystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<CutsceneActor>          _cutsceneActorLookup;
    private ComponentLookup<ActionInterruptRequest> _actionInterruptLookup;
    private ComponentLookup<PathRequest>            _pathRequestLookup;
    private ComponentLookup<Movement>               _movementLookup;
    private ComponentLookup<LocalTransform>         _transformLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _requestQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<CutsceneRequest>()
            .Build(ref state);
        state.RequireForUpdate(_requestQuery);

        _cutsceneActorLookup   = state.GetComponentLookup<CutsceneActor>(false);
        _actionInterruptLookup = state.GetComponentLookup<ActionInterruptRequest>(false);
        _pathRequestLookup     = state.GetComponentLookup<PathRequest>(false);
        _movementLookup        = state.GetComponentLookup<Movement>(false);
        _transformLookup       = state.GetComponentLookup<LocalTransform>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;

        bool hasNarrativeEntity = SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity);

        NativeArray<Entity> signalEntities = _requestQuery.ToEntityArray(Allocator.Temp);

        for (int signalIndex = 0; signalIndex < signalEntities.Length; signalIndex++)
        {
            Entity signalEntity = signalEntities[signalIndex];
            CutsceneRequest request = entityManager.GetComponentData<CutsceneRequest>(signalEntity);

            if (!hasNarrativeEntity)
            {
                Debug.LogWarning($"[CutsceneStartSystem] No NarrativeEventTag singleton in this scene — cutscene {request.cutsceneKey} cannot be tracked, dropping.");
                continue;
            }

            if (entityManager.IsComponentEnabled<ActiveCutscene>(narrativeEntity))
            {
                Debug.LogWarning($"[CutsceneStartSystem] Cutscene {request.cutsceneKey} dropped — another cutscene is already active.");
                continue;
            }

            if (!CutscenePlaybackApi.TryFindStage(entityManager, request.cutsceneKey, out Entity stageEntity))
            {
                Debug.LogWarning($"[CutsceneStartSystem] No CutsceneStage found for key {request.cutsceneKey}.");
                continue;
            }

            Entity playRequestEntity = CutscenePlaybackApi.CreatePlayRequestFromStage(
                entityManager, stageEntity, request.layerIndex, request.speed);

            ApplyBindingOverrides(entityManager, signalEntity, playRequestEntity);

            // CreatePlayRequestFromStage made structural changes above — every ComponentLookup
            // obtained before this point is invalidated and must be refreshed before use.
            _cutsceneActorLookup.Update(ref state);
            _actionInterruptLookup.Update(ref state);
            _pathRequestLookup.Update(ref state);
            _movementLookup.Update(ref state);
            _transformLookup.Update(ref state);

            DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(playRequestEntity);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                Entity boundEntity = bindings[bindingIndex].actorEntity;
                if (boundEntity == Entity.Null)
                    continue;

                GateBoundActor(boundEntity, ref _cutsceneActorLookup, ref _actionInterruptLookup,
                    ref _pathRequestLookup, ref _movementLookup, ref _transformLookup);
            }

            entityManager.SetComponentData(narrativeEntity, new ActiveCutscene { playRequest = playRequestEntity });
            entityManager.SetComponentEnabled<ActiveCutscene>(narrativeEntity, true);
        }

        entityManager.DestroyEntity(signalEntities);
    }

    private static void ApplyBindingOverrides(EntityManager entityManager, Entity signalEntity, Entity playRequestEntity)
    {
        if (!entityManager.HasBuffer<CutsceneRequestBindingOverride>(signalEntity))
            return;

        DynamicBuffer<CutsceneRequestBindingOverride> overrides =
            entityManager.GetBuffer<CutsceneRequestBindingOverride>(signalEntity);
        DynamicBuffer<CutsceneActorBinding> actorBindings =
            entityManager.GetBuffer<CutsceneActorBinding>(playRequestEntity);

        for (int overrideIndex = 0; overrideIndex < overrides.Length; overrideIndex++)
        {
            CutsceneRequestBindingOverride bindingOverride = overrides[overrideIndex];
            bool replacedExisting = false;

            for (int bindingIndex = 0; bindingIndex < actorBindings.Length; bindingIndex++)
            {
                if (actorBindings[bindingIndex].slotId != bindingOverride.slotId)
                    continue;

                actorBindings[bindingIndex] = new CutsceneActorBinding
                {
                    slotId      = bindingOverride.slotId,
                    actorEntity = bindingOverride.target,
                };
                replacedExisting = true;
                break;
            }

            if (!replacedExisting)
            {
                actorBindings.Add(new CutsceneActorBinding
                {
                    slotId      = bindingOverride.slotId,
                    actorEntity = bindingOverride.target,
                });
            }
        }
    }

    private static void GateBoundActor(
        Entity                                      boundEntity,
        ref ComponentLookup<CutsceneActor>          cutsceneActorLookup,
        ref ComponentLookup<ActionInterruptRequest> actionInterruptLookup,
        ref ComponentLookup<PathRequest>            pathRequestLookup,
        ref ComponentLookup<Movement>               movementLookup,
        ref ComponentLookup<LocalTransform>         transformLookup)
    {
        if (cutsceneActorLookup.HasComponent(boundEntity))
            cutsceneActorLookup.SetComponentEnabled(boundEntity, true);

        if (actionInterruptLookup.HasComponent(boundEntity))
            actionInterruptLookup.SetComponentEnabled(boundEntity, true);

        if (pathRequestLookup.HasComponent(boundEntity))
        {
            MovementAPI.HaltPathing(
                ref pathRequestLookup.GetRefRW(boundEntity).ValueRW,
                pathRequestLookup.GetEnabledRefRW<PathRequest>(boundEntity));

            if (movementLookup.HasComponent(boundEntity) && transformLookup.HasComponent(boundEntity))
            {
                movementLookup.GetRefRW(boundEntity).ValueRW.targetPosition = transformLookup[boundEntity].Position;
            }
        }
    }
}
