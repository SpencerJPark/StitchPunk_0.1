using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ChosenActionOptionAuthoring : MonoBehaviour
{
    public class Baker : Baker<ChosenActionOptionAuthoring>
    {
        public override void Bake(ChosenActionOptionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<ChosenActionOption>(entity);
        }
    }
}

public struct ChosenActionOption : IComponentData
{
    public Entity waypoint;
    public Entity previousWaypoint;
    public ActionType actionType;
    public AnimationType animation;
    public float duration;
    public NeedModifiers needModifiers;
    public float3 position;
    public float interactionRange;
}