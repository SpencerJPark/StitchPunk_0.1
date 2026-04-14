using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place ONE of these GameObjects in every scene that uses narrative events.
/// Bakes the NarrativeEvent singleton entity — holds NarrativeEventTag,
/// OnNarrativeEvent, and ActiveNarrativeEvent.
///
/// NarrativeEventManager (MonoBehaviour) and the narrative ECS systems both
/// depend on this singleton entity being present.
/// </summary>
public class NarrativeEventAuthoring : MonoBehaviour
{
    public class Baker : Baker<NarrativeEventAuthoring>
    {
        public override void Bake(NarrativeEventAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new NarrativeEventTag());

            AddComponent(entity, new OnNarrativeEvent());
            SetComponentEnabled<OnNarrativeEvent>(entity, false);

            AddComponent(entity, new ActiveNarrativeEvent());
            SetComponentEnabled<ActiveNarrativeEvent>(entity, false);
        }
    }
}
