using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class SelectedActionAuthoring : MonoBehaviour
{
    public class Baker : Baker<SelectedActionAuthoring>
    {
        public override void Bake(SelectedActionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SelectedAction());
            AddComponent(entity, new NeedsAction());
            SetComponentEnabled<NeedsAction>(entity, false);
        }
    }
}


public struct SelectedAction : IComponentData
{
    public Entity current;
    public Entity previous;
}

public struct NeedsAction : IComponentData, IEnableableComponent
{
}