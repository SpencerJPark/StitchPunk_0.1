using DotsAnimationToolkit;
using Unity.Entities;

/// <summary>
/// Drives <see cref="CutsceneActiveTag"/> from <see cref="ActiveCutscene"/>. NarrativeEventManager
/// independently enables the same tag for a <c>blockPlayerInput</c> narrative event, so the two
/// writers must OR together: this system only ENABLES while a cutscene is active, and only
/// DISABLES when no narrative event is active either — never stomps a narrative-event-driven
/// enable out from under it.
///
/// The one exception is the rendezvous (G2 §3.2): while the clock is paused on a hold and the
/// player still owes it a mark, input goes back to the player so they can walk there themselves.
/// Not [BurstCompile] — the hold state lives behind managed EntityManager reads on the toolkit's
/// request entity.
/// </summary>
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
            // The rendezvous exception deliberately wins over a blockPlayerInput narrative event
            // too: an author who gave the player a mark asked for them to walk to it by hand, and
            // the lock comes straight back the moment the mark resolves.
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
