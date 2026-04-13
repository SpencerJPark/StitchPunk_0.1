using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Starts a dialogue sequence when the player presses Interact while targeting an NPC
/// that has an enabled DialogueProvider.
///
/// Picks the refresher sequence if the primary has already been played (entry in PlayedDialogue buffer).
/// Consumes OnInteractPlayerInput so no other interact system fires on the same frame.
/// Does nothing when a dialogue is already active.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(DialogueSystemGroup))]
public partial struct DialogueStartSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<DialogueManagerTag>();
        state.RequireForUpdate<GameDataTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity managerEntity  = SystemAPI.GetSingletonEntity<DialogueManagerTag>();
        Entity gameDataEntity = SystemAPI.GetSingletonEntity<GameDataTag>();

        // Skip if a dialogue is already running.
        if (SystemAPI.IsComponentEnabled<ActiveDialogue>(managerEntity)) return;

        DynamicBuffer<PlayedDialogue> playedDialogues = SystemAPI.GetBuffer<PlayedDialogue>(gameDataEntity);

        foreach (var (interactEnabled, target, targetEnabled) in
            SystemAPI.Query<
                EnabledRefRW<OnInteractPlayerInput>,
                RefRO<Target>,
                EnabledRefRO<Target>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!interactEnabled.ValueRO) continue;
            if (!targetEnabled.ValueRO)   continue;

            Entity targetEntity = target.ValueRO.entity;

            if (!SystemAPI.HasComponent<DialogueProvider>(targetEntity))         continue;
            if (!SystemAPI.IsComponentEnabled<DialogueProvider>(targetEntity))   continue;

            DialogueProvider provider = SystemAPI.GetComponent<DialogueProvider>(targetEntity);
            if (provider.sequenceId == -1) continue;

            // Use refresher if the primary sequence has been played before.
            int chosenSequenceId = provider.sequenceId;
            if (provider.refresherSequenceId != -1)
            {
                for (int bufferIndex = 0; bufferIndex < playedDialogues.Length; bufferIndex++)
                {
                    if (playedDialogues[bufferIndex].sequenceId == provider.sequenceId)
                    {
                        chosenSequenceId = provider.refresherSequenceId;
                        break;
                    }
                }
            }

            SystemAPI.SetComponent<ActiveDialogue>(managerEntity, new ActiveDialogue
            {
                sequenceId    = chosenSequenceId,
                speakerEntity = targetEntity,
            });
            SystemAPI.SetComponentEnabled<ActiveDialogue>(managerEntity, true);

            // Consume interact so nothing else (e.g. PlayerReviverSystem) fires this frame.
            interactEnabled.ValueRW = false;
            break;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
