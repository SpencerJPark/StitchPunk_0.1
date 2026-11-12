using Unity.Entities;
using UnityEngine;

public class OutlineChildAuthoring : MonoBehaviour {

    public GameObject outlineParent;
    
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<OutlineChildAuthoring> {
        public override void Bake(OutlineChildAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new OutlineChild
            {
                parentEntity = GetEntity(authoring.outlineParent, TransformUsageFlags.Dynamic),
            });
            AddComponent(entity, new OutlinedTag());
            SetComponentEnabled<OutlinedTag>(entity, false);
        }
    }
}


