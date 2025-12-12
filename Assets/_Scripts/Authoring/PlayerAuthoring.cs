using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour {
    
    public class Baker : Baker<PlayerAuthoring> {

        public override void Bake(PlayerAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player
            {
                interactableEntity = Entity.Null,
            });
        }
    }
}

public struct Player : IComponentData {
    
    public Entity interactableEntity; // entity player can interact with
}