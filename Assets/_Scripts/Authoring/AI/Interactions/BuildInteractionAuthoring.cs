using Unity.Entities;
using UnityEngine;

public class BuildInteractionAuthoring : MonoBehaviour
{
    public class Baker : Baker<BuildInteractionAuthoring>
    {
        public override void Bake(BuildInteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BuildTask());
            SetComponentEnabled<BuildTask>(entity, false);
        }
    }
}