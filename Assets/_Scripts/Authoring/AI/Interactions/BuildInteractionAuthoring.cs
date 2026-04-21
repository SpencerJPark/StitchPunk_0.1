using Unity.Entities;
using UnityEngine;

public class BuildInteractionAuthoring : MonoBehaviour
{
    public class Baker : Baker<BuildInteractionAuthoring>
    {
        public override void Bake(BuildInteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BuildInteraction());
            SetComponentEnabled<BuildInteraction>(entity, false);
        }
    }
}