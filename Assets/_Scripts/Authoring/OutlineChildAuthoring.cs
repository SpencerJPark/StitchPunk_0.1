using Unity.Entities;
using UnityEngine;

public class OutlineChildAuthoring : MonoBehaviour {

    public GameObject outlineParent;
    
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


public struct OutlineChild : IComponentData {
    public Entity parentEntity;
}

/// <summary>
/// When this component is present and enabled, the entity renders to the outline camera
/// </summary>
public struct OutlinedTag : IComponentData, IEnableableComponent
{
}