using Unity.Entities;
using UnityEngine;

public class ActionLockAuthoring : MonoBehaviour
{
    public float maxActionDuration = 30f;
    public float stuckThreshold = 1f;
    public float stuckTime = 3f;

    public class Baker : Baker<ActionLockAuthoring>
    {
        public override void Bake(ActionLockAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<ActionLock>(entity, new ActionLock
            {
                lockedAction = ActionType.None,
                isComplete = false,
                maxDuration = authoring.maxActionDuration,
                timer = 0f,
                stuckThreshold = authoring.stuckThreshold,
                stuckTime = authoring.stuckTime,
                stuckTimer = 0f,
                lastPosition = Unity.Mathematics.float3.zero
            });
        }
    }
}

public struct ActionLock : IComponentData
{
    public ActionType lockedAction;
    public bool isComplete;
    public float maxDuration;
    public float timer;
    public float stuckThreshold;
    public float stuckTime;
    public float stuckTimer;
    public Unity.Mathematics.float3 lastPosition;
}