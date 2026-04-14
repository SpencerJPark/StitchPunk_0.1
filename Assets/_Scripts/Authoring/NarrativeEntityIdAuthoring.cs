using Unity.Entities;
using UnityEngine;

/// <summary>
/// Gives any entity a stable integer ID so NarrativeEventSO actions can address
/// it without storing an Entity handle in the ScriptableObject.
///
/// Add this to any NPC or waypoint GameObject that a narrative action needs to target.
/// Assign an ID that matches a constant in NarrativeIds.Entities.
///
/// NarrativeEventManager builds a Dictionary&lt;int, Entity&gt; from all NarrativeEntityId
/// components at startup for O(1) lookup during event execution.
/// </summary>
public class NarrativeEntityIdAuthoring : MonoBehaviour
{
    [Tooltip("Unique entity ID for this NPC or waypoint. " +
             "Must match a constant in NarrativeIds.Entities. " +
             "Never reuse an ID — each entity must have a unique value.")]
    public int id;

    public class Baker : Baker<NarrativeEntityIdAuthoring>
    {
        public override void Bake(NarrativeEntityIdAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new NarrativeEntityId { id = authoring.id });
        }
    }
}
