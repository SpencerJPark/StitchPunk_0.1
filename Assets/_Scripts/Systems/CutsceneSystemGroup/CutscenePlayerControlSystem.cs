using DotsAnimationToolkit;
using Unity.Entities;

// NarrativeEventManager also enables CutsceneActiveTag, so this system only disables it when no
// narrative event is active either. Not [BurstCompile]: hold state is read through managed EntityManager.
[UpdateInGroup(typeof(CutsceneSystemGroup))]
[UpdateAfter(typeof(CutsceneEndSystem))]
public partial struct CutscenePlayerControlSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity))
            return;

        if (SystemAPI.IsComponentEnabled<ActiveCutscene>(narrativeEntity))
        {
            // Rendezvous: input returns to the player while a hold waits on their mark, even over
            // a blockPlayerInput event. The lock returns when the mark resolves.
            bool playerOwesTheHoldAMark = IsHoldWaitingOnThePlayersOwnMark(ref state, narrativeEntity);
            SystemAPI.SetComponentEnabled<CutsceneActiveTag>(narrativeEntity, !playerOwesTheHoldAMark);
            return;
        }

        if (!SystemAPI.IsComponentEnabled<ActiveNarrativeEvent>(narrativeEntity))
            SystemAPI.SetComponentEnabled<CutsceneActiveTag>(narrativeEntity, false);
    }

    private bool IsHoldWaitingOnThePlayersOwnMark(ref SystemState state, Entity narrativeEntity)
    {
        if (!SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
            return false;

        EntityManager entityManager = state.EntityManager;
        if (!entityManager.HasComponent<CutsceneMoveToMark>(playerEntity)
            || !entityManager.IsComponentEnabled<CutsceneMoveToMark>(playerEntity))
        {
            return false;
        }

        Entity playRequestEntity = SystemAPI.GetComponent<ActiveCutscene>(narrativeEntity).playRequest;
        if (!entityManager.Exists(playRequestEntity)
            || !entityManager.HasComponent<CutscenePlaybackState>(playRequestEntity))
        {
            return false;
        }

        return entityManager.GetComponentData<CutscenePlaybackState>(playRequestEntity).isPausedOnHold;
    }
}
