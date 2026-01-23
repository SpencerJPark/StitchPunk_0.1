using Unity.Entities;
using UnityEngine;

public class SelectedActionAuthoring : MonoBehaviour
{
    public class Baker : Baker<SelectedActionAuthoring>
    {
        public override void Bake(SelectedActionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SelectedAction());
        }
    }
}

public struct SelectedAction : IComponentData
{
    public ActionType current;
    public ActionType previous;
}