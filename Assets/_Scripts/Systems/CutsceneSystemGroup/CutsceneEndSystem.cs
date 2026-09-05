using DotsAnimationToolkit;
using Unity.Entities;

/// <summary>
/// Tears down a completed cutscene: releases every bound actor back to AI control and destroys
/// the toolkit's playback request (the toolkit leaves that lifecycle step to the host).
/// Not [BurstCompile] — structural change (DestroyEntity).
/// </summary>
[UpdateInGroup(typeof(CutsceneSystemGroup))]
[UpdateAfter(typeof(CutsceneStartSystem))]
public partial struct CutsceneEndSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity))
            return;
        if (!SystemAPI.IsComponentEnabled<ActiveCutscene>(narrativeEntity))
            return;

        EntityManager entityManager = state.EntityManager;
        Entity playRequestEntity = SystemAPI.GetComponent<ActiveCutscene>(narrativeEntity).playRequest;

        bool isComplete = !entityManager.Exists(playRequestEntity)
            || !entityManager.HasComponent<CutscenePlaybackState>(playRequestEntity)
            || entityManager.GetComponentData<CutscenePlaybackState>(playRequestEntity).isComplete;

        if (!isComplete)
            return;

        if (entityManager.Exists(playRequestEntity) && entityManager.HasBuffer<CutsceneActorBinding>(playRequestEntity))
        {
            DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(playRequestEntity);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                Entity boundEntity = bindings[bindingIndex].actorEntity;
                if (boundEntity == Entity.Null || !entityManager.Exists(boundEntity))
                    continue;

                ReleaseBoundActor(entityManager, boundEntity);
            }
        }

        if (entityManager.Exists(playRequestEntity))
            entityManager.DestroyEntity(playRequestEntity);

        SystemAPI.SetComponentEnabled<ActiveCutscene>(narrativeEntity, false);
    }

    // Re-arms the brain the way ReviveRequestSystem/SwapBrainSystem do after teardown (Systems_AI.md):
    // force a clean Idle through the single interrupt path, and trigger an immediate awareness pass
    // instead of waiting for the next periodic ActionRequest tick.
    private static void ReleaseBoundActor(EntityManager entityManager, Entity boundEntity)
    {
        if (entityManager.HasComponent<CutsceneActor>(boundEntity))
            entityManager.SetComponentEnabled<CutsceneActor>(boundEntity, false);

        if (entityManager.HasComponent<ActionInterruptRequest>(boundEntity))
            entityManager.SetComponentEnabled<ActionInterruptRequest>(boundEntity, true);

        if (entityManager.HasComponent<ActionRequest>(boundEntity))
            entityManager.SetComponentEnabled<ActionRequest>(boundEntity, true);
    }
}
