using Unity.Entities;
using UnityEngine;

/// <summary>
/// Add to a GameObject to create a proximity trigger for a narrative event.
/// When the player comes within range, NarrativeProximitySystem fires the event
/// specified by eventId and disables this trigger.
///
/// If repeatable is true, NarrativeEventManager re-enables the trigger once the
/// event finishes so it can fire again on the player's next approach.
///
/// The eventId must match a constant in NarrativeIds.Events.
/// </summary>
public class NarrativeEventTriggerAuthoring : MonoBehaviour
{
    [Tooltip("Which narrative event to fire when the player enters range. " +
             "Must match a constant in NarrativeIds.Events.")]
    public int eventId;

    [Tooltip("How close (in world units) the player must be for this trigger to fire.")]
    public float range = 3f;

    [Tooltip("When true the trigger re-arms after the event completes, allowing it to fire again. " +
             "When false it fires once and stays disabled permanently.")]
    public bool repeatable;

    public class Baker : Baker<NarrativeEventTriggerAuthoring>
    {
        public override void Bake(NarrativeEventTriggerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new NarrativeTrigger
            {
                eventId    = authoring.eventId,
                range      = authoring.range,
                repeatable = authoring.repeatable,
            });
            SetComponentEnabled<NarrativeTrigger>(entity, true);
        }
    }
}
