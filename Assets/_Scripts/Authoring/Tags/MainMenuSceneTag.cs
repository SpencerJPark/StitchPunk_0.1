using Unity.Entities;
using UnityEngine;

public class MainMenuTagAuthoring : MonoBehaviour {
    
    public class Baker : Baker<MainMenuTagAuthoring> {
        
        public override void Bake(MainMenuTagAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new MainMenuTag());
        }
    }
}

