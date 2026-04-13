using Unity.Entities;
using UnityEngine;

/// <summary>
/// Add to an NPC GameObject to give it player-triggerable dialogue.
/// Bakes DialogueProvider onto the entity.
///
/// PlayerInteractable:
///   - Added automatically UNLESS the same GO also has an InteractionAuthoring
///     with playerInteractable = true (which adds it instead, avoiding duplicates).
///   - If you have InteractionAuthoring on this GO, set its playerInteractable = true
///     so the NPC appears in the player's targeting system.
/// </summary>
public class DialogueProviderAuthoring : MonoBehaviour
{
    [Tooltip("The primary dialogue sequence for this NPC. Must have a unique ID set in DialogueIds.Sequences.")]
    public DialogueSequenceSO dialogueSequence;

    public class Baker : Baker<DialogueProviderAuthoring>
    {
        public override void Bake(DialogueProviderAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            int primaryId = authoring.dialogueSequence != null ? authoring.dialogueSequence.sequenceId : -1;
            int refresherId = (authoring.dialogueSequence != null && authoring.dialogueSequence.refresherSequence != null)
                ? authoring.dialogueSequence.refresherSequence.sequenceId
                : -1;

            AddComponent(entity, new DialogueProvider
            {
                sequenceId          = primaryId,
                refresherSequenceId = refresherId,
            });
            SetComponentEnabled<DialogueProvider>(entity, true);

            // Only add PlayerInteractable if InteractionAuthoring won't add it.
            // This prevents duplicate-component bake errors on NPCs with both components.
            InteractionAuthoring interactionAuth = GetComponent<InteractionAuthoring>();
            bool interactionWillAddIt = interactionAuth != null && interactionAuth.playerInteractable;
            if (!interactionWillAddIt)
            {
                AddComponent(entity, new PlayerInteractable());
            }
        }
    }
}
