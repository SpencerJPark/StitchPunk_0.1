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

    [Tooltip("Which per-kind execution system handles this interaction once a unit commits to it. " +
             "None falls back to the universal InteractionExecutionSystem.")]
    public InteractionKind kind = InteractionKind.None;

    [Header("Player")]
    [Tooltip("Whether the player can directly target and interact with this entity.")]
    public bool playerInteractable;

    [Header("Behaviour Satisfaction")]
    [Tooltip("Which NPC behaviours this interaction satisfies and by how much. " +
             "An entry with value 0 is skipped. Value is remapped to multiplier = value*0.01 + 1.")]
    public BehaviourEntry[] satisfies = System.Array.Empty<BehaviourEntry>();

    [System.Serializable]
    public struct BehaviourEntry
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

            AddComponent(entity, new InteractionKindData
            {
                kind = authoring.kind,
            });
            

            AddComponent(entity, new InteractionProvider());
            SetComponentEnabled<InteractionProvider>(entity, true);
            

            AddComponent(entity, new InteractionHandled());
            SetComponentEnabled<InteractionHandled>(entity, false);

            if (authoring.playerInteractable) AddComponent(entity, new PlayerInteractable());

            // BehaviourSatisfaction buffer — one entry per behaviour this provider can satisfy.
            // SpatialHashSystem registers the entity under each listed behaviourType; scoring
            // reads the matching multiplier during final-score composition.
            DynamicBuffer<BehaviourSatisfaction> satisfactionBuffer =
                AddBuffer<BehaviourSatisfaction>(entity);

            if (authoring.satisfies != null)
            {
                for (int i = 0; i < authoring.satisfies.Length; i++)
                {
                    BehaviourEntry entry = authoring.satisfies[i];
                    if (entry.motivationType == MotivationType.None)
                        continue;

                    satisfactionBuffer.Add(new BehaviourSatisfaction
                    {
                        motivationType = entry.motivationType,
                        multiplier    = entry.value * 0.01f + 1f,
                    });
                }
            }
        }
    }
}
