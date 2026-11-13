using Unity.Burst;
using Unity.Entities;

// DialogueUIManager (MonoBehaviour, runs after ECS) enables OnDialogueEvent, so handlers see it
// next frame; this system clears it at the end of that frame. Handlers read eventId vs DialogueIds.Events.
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

        SystemAPI.SetComponentEnabled<OnDialogueEvent>(managerEntity, false);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
