using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Turns a cutscene's <c>AnimEvents.Dialogue</c> cue into a real dialogue, and gives the clock back
/// when the player closes it. The payload is the one G2 §4 fixed: <c>intParam</c> is the sequence id,
/// <c>floatParam</c> is the speaker's slot index (−1 for nobody).
///
/// The cue's hold is named after the event itself (A65), so the id to release is literally
/// "Dialogue" — <c>CutscenePlaybackApi.TryGetCurrentHoldId</c> is what confirms the clock is actually
/// waiting on it. A cue authored without <c>holdUntilReleased</c> still opens a dialogue; it just
/// does not stop the cutscene.
///
/// Reads last frame's events: <c>CutsceneTimelineSystem</c> writes <c>AnimEventOutput</c> from the
/// toolkit group, which runs after this one — the one-frame latency documented in Contracts.md.
/// Not [BurstCompile] — managed EntityManager reads and the toolkit's playback API.
/// </summary>
[UpdateInGroup(typeof(CutsceneSystemGroup))]
[UpdateAfter(typeof(CutsceneStartSystem))]
public partial struct CutsceneDialogueCueSystem : ISystem
{
    // The hold a Dialogue cue derives, which is the event's own registry name (A65 §3.1).
    private const string DialogueHoldId = "Dialogue";

    // Whether a cue this system started is still on screen. Clock state cannot answer this: a hold
    // is released the moment the dialogue closes, and a cue that never held one has no clock trace
    // at all.
    private bool _cuedDialogueIsOpen;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DialogueManagerTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<NarrativeEventTag>(out Entity narrativeEntity))
            return;

        if (!SystemAPI.IsComponentEnabled<ActiveCutscene>(narrativeEntity))
        {
            _cuedDialogueIsOpen = false;
            return;
        }

        EntityManager entityManager = state.EntityManager;
        Entity playRequestEntity = SystemAPI.GetComponent<ActiveCutscene>(narrativeEntity).playRequest;
        if (!entityManager.Exists(playRequestEntity) || !entityManager.HasBuffer<AnimEventOutput>(playRequestEntity))
            return;

        Entity dialogueManagerEntity = SystemAPI.GetSingletonEntity<DialogueManagerTag>();

        if (entityManager.IsComponentEnabled<AnimEventsPending>(playRequestEntity))
        {
            StartCuedDialogues(entityManager, playRequestEntity, dialogueManagerEntity, ref _cuedDialogueIsOpen);
        }

        if (_cuedDialogueIsOpen && !entityManager.IsComponentEnabled<ActiveDialogue>(dialogueManagerEntity))
        {
            _cuedDialogueIsOpen = false;
            ReleaseTheDialogueHold(entityManager, playRequestEntity);
        }
    }

    // The same write NarrativeEventManager.ExecuteDialogueTriggerAsync makes, so the UI manager
    // cannot tell a cutscene cue from an authored dialogue trigger.
    private static void StartCuedDialogues(
        EntityManager entityManager,
        Entity playRequestEntity,
        Entity dialogueManagerEntity,
        ref bool cuedDialogueIsOpen)
    {
        DynamicBuffer<AnimEventOutput> cutsceneEvents = entityManager.GetBuffer<AnimEventOutput>(playRequestEntity);
        for (int eventIndex = 0; eventIndex < cutsceneEvents.Length; eventIndex++)
        {
            AnimEventOutput cutsceneEvent = cutsceneEvents[eventIndex];
            if (cutsceneEvent.eventKey != AnimEvents.Dialogue)
                continue;

            entityManager.SetComponentData(dialogueManagerEntity, new ActiveDialogue
            {
                sequenceId    = cutsceneEvent.intParam,
                speakerEntity = ResolveSpeakerEntity(entityManager, playRequestEntity, (int)cutsceneEvent.floatParam),
            });
            entityManager.SetComponentEnabled<ActiveDialogue>(dialogueManagerEntity, true);
            cuedDialogueIsOpen = true;
        }
    }

    private static void ReleaseTheDialogueHold(EntityManager entityManager, Entity playRequestEntity)
    {
        if (!CutscenePlaybackApi.TryGetCurrentHoldId(entityManager, playRequestEntity, out FixedString64Bytes holdId))
            return;
        if (holdId != new FixedString64Bytes(DialogueHoldId))
            return;

        entityManager.SetComponentData(playRequestEntity, new CutsceneHoldRelease { holdId = holdId });
        entityManager.SetComponentEnabled<CutsceneHoldRelease>(playRequestEntity, true);
    }

    // The payload carries a slot INDEX, because that is what an author picks in the cutscene editor;
    // bindings are keyed by slot id, and blob.slots is in authored order, so the index resolves
    // through it rather than by position in a binding buffer a host is free to append to.
    private static Entity ResolveSpeakerEntity(
        EntityManager entityManager, Entity playRequestEntity, int speakerSlotIndex)
    {
        if (speakerSlotIndex < 0 || !entityManager.HasComponent<CutscenePlay>(playRequestEntity))
            return Entity.Null;

        CutscenePlay play = entityManager.GetComponentData<CutscenePlay>(playRequestEntity);
        if (!play.blob.IsCreated)
            return Entity.Null;

        ref CutsceneBlob blob = ref play.blob.Value;
        if (speakerSlotIndex >= blob.slots.Length)
            return Entity.Null;

        uint speakerSlotId = blob.slots[speakerSlotIndex].slotId;
        DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(playRequestEntity);
        for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
        {
            if (bindings[bindingIndex].slotId == speakerSlotId)
                return bindings[bindingIndex].actorEntity;
        }
        return Entity.Null;
    }
}
