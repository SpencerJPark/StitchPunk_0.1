using Unity.Entities;
using UnityEngine;

// Bakes the undead-specific components onto a unit body entity.
// Add this alongside MinionAuthoring on zombie prefabs, but it can exist
// independently on any entity that can be revived without being player-controllable.
public class UndeadAuthoring : MonoBehaviour
{
    public bool startUndead;

    public class Baker : Baker<UndeadAuthoring>
    {
        public override void Bake(UndeadAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Undead>(entity);
            SetComponentEnabled<Undead>(entity, authoring.startUndead);

            AddComponent<ReviveRequest>(entity);
            SetComponentEnabled<ReviveRequest>(entity, false);

            // Make a revivable unit targetable by the reviver — but only once it dies.
            // Baked disabled; DeathSystem enables it on death, ReviveRequestSystem disables it on revive.
            // Skip if another authoring on this GO already provides PlayerInteractable (avoids dup-bake).
            InteractionAuthoring interactionAuth = GetComponent<InteractionAuthoring>();
            DialogueProviderAuthoring dialogueAuth = GetComponent<DialogueProviderAuthoring>();
            bool alreadyProvided = (interactionAuth != null && interactionAuth.playerInteractable) || dialogueAuth != null;
            if (!alreadyProvided)
            {
                AddComponent(entity, new PlayerInteractable());
                SetComponentEnabled<PlayerInteractable>(entity, false);
            }
        }
    }
}
