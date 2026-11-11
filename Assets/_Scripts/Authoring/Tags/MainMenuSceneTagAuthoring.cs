using Unity.Entities;
using UnityEngine;

public class MainMenuSceneTagAuthoring : MonoBehaviour {
    
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<MainMenuSceneTagAuthoring> {
        
        public override void Bake(MainMenuSceneTagAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new MainMenuTag());
        }
    }
}

