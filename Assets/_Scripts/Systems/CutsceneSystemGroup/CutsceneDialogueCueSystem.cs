using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;

// Dialogue cue payload: intParam is the sequence id, floatParam is the speaker slot index (-1 for nobody).
// Reads last frame's AnimEventOutput (the toolkit group runs after this one), so cues have one frame of latency.
// Not [BurstCompile]: managed EntityManager reads and the toolkit playback API.
[UpdateInGroup(typeof(CutsceneSystemGroup))]
[UpdateAfter(typeof(CutsceneStartSystem))]
public partial struct CutsceneDialogueCueSystem : ISystem
{
    // A Dialogue cue's hold id is the event's own registry name.
    private const string DialogueHoldId = "Dialogue";

    // Clock state cannot say whether the dialogue is still open: a cue with no hold leaves no trace.
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

    // Same write as NarrativeEventManager.ExecuteDialogueTriggerAsync, so the UI cannot tell the two apart.
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
        if (!CutsceneApi.TryGetCurrentHoldId(entityManager, playRequestEntity, out FixedString64Bytes holdId))
            return;
        if (holdId != new FixedString64Bytes(DialogueHoldId))
            return;

        entityManager.SetComponentData(playRequestEntity, new CutsceneHoldRelease { holdId = holdId });
        entityManager.SetComponentEnabled<CutsceneHoldRelease>(playRequestEntity, true);
    }

    // The payload is a slot index into blob.slots (authored order), not a position in the binding buffer, which hosts may append to.
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
