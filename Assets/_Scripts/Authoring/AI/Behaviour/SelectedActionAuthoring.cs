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
    public ActionType current;
    public ActionType previous;
    public bool startedAction;
    public float maxDuration;
    public float timer;
    public float stuckThreshold;
    public float stuckTime;
    public float stuckTimer;
    public float3 lastPosition;
    public float decisionInterval;
    public float decisionTimer;
}

public struct NeedsAction : IComponentData, IEnableableComponent
{
}