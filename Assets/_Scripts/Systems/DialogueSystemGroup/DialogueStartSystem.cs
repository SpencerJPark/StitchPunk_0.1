using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Starts a dialogue sequence when the player presses Interact while targeting an NPC
/// that has an enabled DialogueProvider.
///
/// Always passes the primary sequenceId — the UIManager picks the Refresher entry node
/// if the sequence has already been played.
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
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity managerEntity = SystemAPI.GetSingletonEntity<DialogueManagerTag>();

        // Skip if a dialogue is already running.
        if (SystemAPI.IsComponentEnabled<ActiveDialogue>(managerEntity)) return;

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

            SystemAPI.SetComponent<ActiveDialogue>(managerEntity, new ActiveDialogue
            {
                sequenceId    = provider.sequenceId,
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
