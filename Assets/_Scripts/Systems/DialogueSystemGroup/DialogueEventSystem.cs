using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Keeps OnDialogueEvent enabled for exactly one ECS frame so downstream systems
/// (e.g. TournamentUnlockSystem) can react, then disables it.
///
/// OnDialogueEvent is enabled by DialogueUIManager (MonoBehaviour) when a dialogue
/// choice fires a game event. Because MonoBehaviours run after ECS in the default
/// player loop, the event is processed by downstream systems on the NEXT frame,
/// then cleared here.
///
/// To handle a dialogue event in a game feature system:
///   [WithAll(typeof(OnDialogueEvent))]
///   partial struct MyHandlerJob : IJobEntity { ... }
/// Then read dialogueEvent.eventId and compare to constants in DialogueIds.Events.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(DialogueSystemGroup), OrderLast = true)]
public partial struct DialogueEventSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DialogueManagerTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity managerEntity = SystemAPI.GetSingletonEntity<DialogueManagerTag>();

        if (!SystemAPI.IsComponentEnabled<OnDialogueEvent>(managerEntity)) return;

        // Disable after one frame so downstream systems had a chance to react.
        SystemAPI.SetComponentEnabled<OnDialogueEvent>(managerEntity, false);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
