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

    [Tooltip("Which unit types may use this interaction. Leave empty to allow all unit types.")]
    public UnitType[] allowedUnitTypes = System.Array.Empty<UnitType>();

    [Header("Player")]
    [Tooltip("Whether the player can directly target and interact with this entity.")]
    public bool playerInteractable;

    [Header("Motivation Satisfaction")]
    [Tooltip("Which NPC motivations this interaction satisfies and by how much (flat units, 0–100). " +
             "An entry with value 0 is skipped.")]
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

            MotivationType primaryMotivation = MotivationType.None;
            float primaryUtility = 0f;
            if (authoring.satisfies != null)
            {
                for (int i = 0; i < authoring.satisfies.Length; i++)
                {
                    if (authoring.satisfies[i].motivationType != MotivationType.None)
                    {
                        primaryMotivation = authoring.satisfies[i].motivationType;
                        primaryUtility    = authoring.satisfies[i].value;
                        break;
                    }
                }
            }

            uint unitMask = 0u;
            if (authoring.allowedUnitTypes != null)
            {
                for (int i = 0; i < authoring.allowedUnitTypes.Length; i++)
                    unitMask |= 1u << (int)authoring.allowedUnitTypes[i];
            }

            AddComponent(entity, new Interaction
            {
                actionType           = authoring.actionType,
                motivationType       = primaryMotivation,
                utilityScore         = primaryUtility,
                range                = authoring.interactionRange,
                allowedUnitTypeMask  = unitMask,
                maxOccupants         = authoring.maxOccupant,
                occupantCount        = 0
            });

            

            AddComponent(entity, new InteractionProvider());
            SetComponentEnabled<InteractionProvider>(entity, true);
            

            if (authoring.playerInteractable) AddComponent(entity, new PlayerInteractable());

            // MotivationSatisfaction buffer — one entry per motivation this provider can restore.
            // SpatialHashSystem registers the entity under each motivation type for spatial queries.
            // SitActionSystem reads restorationAmount on completion and queues MotivationChangeRequests.
            DynamicBuffer<MotivationSatisfaction> satisfactionBuffer =
                AddBuffer<MotivationSatisfaction>(entity);

            if (authoring.satisfies != null)
            {
                for (int i = 0; i < authoring.satisfies.Length; i++)
                {
                    MotivationEntry entry = authoring.satisfies[i];
                    if (entry.motivationType == MotivationType.None || entry.value == 0)
                        continue;

                    satisfactionBuffer.Add(new MotivationSatisfaction
                    {
                        motivationType    = entry.motivationType,
                        restorationAmount = entry.value,
                    });
                }
            }
        }
    }
}
