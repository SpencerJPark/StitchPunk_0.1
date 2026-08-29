using DotsMovementToolkit;
using Unity.Entities;
using UnityEngine;

namespace DotsMovementToolkit.Authoring
{
    public class HordeRegistryAuthoring : MonoBehaviour {

        public class Baker : Baker<HordeRegistryAuthoring> {

            public override void Bake(HordeRegistryAuthoring authoring) {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new HordeRegistry());
            }
        }
    }
}
