using Unity.Entities;
using UnityEngine;

public class PlayerAuthoring : MonoBehaviour {
    
    public GameObject testSelectGameObject;
    
    public class Baker : Baker<PlayerAuthoring> {

        public override void Bake(PlayerAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player
            {
                interactableEntity = GetEntity(authoring.testSelectGameObject, TransformUsageFlags.Dynamic),
            });
        }
    }
}

public struct Player : IComponentData {
    
    public Entity interactableEntity; // entity player can interact with
}