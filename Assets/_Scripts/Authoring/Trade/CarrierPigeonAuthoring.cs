using Unity.Entities;
using UnityEngine;

// On the pigeon prefab. PigeonFlightSystem flies it to the player's arm; there is nothing to interact with.
public class CarrierPigeonAuthoring : MonoBehaviour
{
    public class Baker : Baker<CarrierPigeonAuthoring>
    {
        public override void Bake(CarrierPigeonAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new CarrierPigeon());
            AddComponent(entity, new PigeonFlight());
        }
    }
}
