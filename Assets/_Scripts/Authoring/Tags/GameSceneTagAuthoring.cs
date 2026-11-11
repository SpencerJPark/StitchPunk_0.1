using Unity.Entities;
using UnityEngine;

public class GameSceneTagAuthoring : MonoBehaviour {


    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<GameSceneTagAuthoring> {


        public override void Bake(GameSceneTagAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new GameSceneTag());
        }
    }
}

