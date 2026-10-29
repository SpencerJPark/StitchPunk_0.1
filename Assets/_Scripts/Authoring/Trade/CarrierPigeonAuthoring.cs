using Unity.Entities;
using UnityEngine;

// On the pigeon prefab. Interaction stays off until the pigeon lands (PigeonFlightSystem enables it).
public class CarrierPigeonAuthoring : MonoBehaviour
{
    [Tooltip("Inside this planar radius the pigeon is picked over a nearer interactable (the caravan it sits on).")]
    public float priorityRadius = 4f;

    public class Baker : Baker<CarrierPigeonAuthoring>
    {
        public override void Bake(CarrierPigeonAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new CarrierPigeon());
            AddComponent(entity, new PigeonFlight());
            AddComponent<PlayerInteractable>(entity);
            SetComponentEnabled<PlayerInteractable>(entity, false);
            AddComponent(entity, new PlayerInteractablePriority { priorityRadius = authoring.priorityRadius });
        }
    }
}
