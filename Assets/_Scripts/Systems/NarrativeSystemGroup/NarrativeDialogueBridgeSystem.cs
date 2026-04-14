using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Bridges dialogue events to narrative events.
///
/// When OnDialogueEvent is enabled and its eventId appears in NarrativeIds.DialogueBridge.Pairs,
/// this system enables OnNarrativeEvent with the mapped narrative event ID.
///
/// Runs OrderFirst in NarrativeSystemGroup to ensure the bridge fires before NarrativeProximitySystem
/// could overwrite OnNarrativeEvent in the same frame. Also runs before DialogueSystemGroup so it
/// reads OnDialogueEvent before DialogueEventSystem disables it (DialogueEventSystem is OrderLast
/// in DialogueSystemGroup, which runs after NarrativeSystemGroup).
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(NarrativeSystemGroup), OrderFirst = true)]
public partial struct NarrativeDialogueBridgeSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NarrativeEventTag>();
        state.RequireForUpdate<DialogueManagerTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity dialogueEntity  = SystemAPI.GetSingletonEntity<DialogueManagerTag>();
        Entity narrativeEntity = SystemAPI.GetSingletonEntity<NarrativeEventTag>();

        // Nothing to bridge if no dialogue event fired this frame.
        if (!SystemAPI.IsComponentEnabled<OnDialogueEvent>(dialogueEntity)) return;

        // Nothing to bridge if a narrative event is already queued or running.
        if (SystemAPI.IsComponentEnabled<OnNarrativeEvent>(narrativeEntity)) return;
        if (SystemAPI.IsComponentEnabled<ActiveNarrativeEvent>(narrativeEntity)) return;

        int dialogueEventId = SystemAPI.GetComponent<OnDialogueEvent>(dialogueEntity).eventId;

        (int dialogueEventId, int narrativeEventId)[] pairs = NarrativeIds.DialogueBridge.Pairs;
        for (int pairIndex = 0; pairIndex < pairs.Length; pairIndex++)
        {
            if (pairs[pairIndex].dialogueEventId != dialogueEventId) continue;

            SystemAPI.SetComponent(narrativeEntity, new OnNarrativeEvent
            {
                eventId = pairs[pairIndex].narrativeEventId,
            });
            SystemAPI.SetComponentEnabled<OnNarrativeEvent>(narrativeEntity, true);
            break;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
