using Unity.Entities;
using UnityEngine;

public class PlayerCharacterAuthoring : MonoBehaviour {


    public class Baker : Baker<PlayerCharacterAuthoring> {


        public override void Bake(PlayerCharacterAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerCharacter());
        }
    }
}




public struct PlayerCharacter : IComponentData {
}