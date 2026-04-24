using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

public class InteractionAuthoring : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("How close the NPC must be to start the action")]
    public float interactionRange = 1.5f;

    public int maxOccupant = 1;

    public float maxTime = 0.5f;

    [Tooltip("Legacy animation/state hint copied onto UnitAction. Orthogonal to InteractionKind.")]
    public ActionType actionType = ActionType.Interact;
    

    [Header("Player")]
    [Tooltip("Whether the player can directly target and interact with this entity.")]
    public bool playerInteractable;

    [Header("Motivation Satisfaction")]
    [Tooltip("Which NPC behaviours this interaction satisfies and by how much. " +
             "An entry with value 0 is skipped. Value is remapped to multiplier = value*0.01 + 1.")]
    public MotivationEntry[] satisfies = System.Array.Empty<MotivationEntry>();

    [System.Serializable]
    public struct MotivationEntry
    {
        [FormerlySerializedAs("behaviourType")] public MotivationType motivationType;
        public int value;
    }

    public class Baker : Baker<InteractionAuthoring>
    {
        public override void Bake(InteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Interaction
            {
                actionType = authoring.actionType,
                maxOccupants = authoring.maxOccupant
            });

            

            AddComponent(entity, new InteractionProvider());
            SetComponentEnabled<InteractionProvider>(entity, true);
            

            if (authoring.playerInteractable) AddComponent(entity, new PlayerInteractable());

            // MotivationSatisfaction buffer — one entry per behaviour this provider can satisfy.
            // SpatialHashSystem registers the entity under each listed behaviourType; scoring
            // reads the matching multiplier during final-score composition.
            DynamicBuffer<MotivationSatisfaction> satisfactionBuffer =
                AddBuffer<MotivationSatisfaction>(entity);

            if (authoring.satisfies != null)
            {
                for (int i = 0; i < authoring.satisfies.Length; i++)
                {
                    MotivationEntry entry = authoring.satisfies[i];
                    if (entry.motivationType == MotivationType.None)
                        continue;

                    satisfactionBuffer.Add(new MotivationSatisfaction
                    {
                        motivationType = entry.motivationType,
                        multiplier    = entry.value * 0.01f + 1f,
                    });
                }
            }
        }
    }
}
