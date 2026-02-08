using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class CitizenBrainAuthoring : MonoBehaviour
{
    [Header("Wander Settings")]
    public float wanderRadius = 10f;

    [Header("Action Lock Settings")]
    public float maxActionDuration = 30f;
    public float stuckThreshold = 1f;
    public float stuckTime = 3f;

    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<CitizenBrain>(entity);

            AddComponent<CanEat>(entity);
            AddComponent<CanSleep>(entity);
            AddComponent<CanSocialize>(entity);
            AddComponent<CanWander>(entity);
            AddComponent<CanRoam>(entity);

            AddComponent<WanderState>(entity, new WanderState
            {
                wanderRadius = authoring.wanderRadius,
                wanderTarget = float3.zero
            });

            AddComponent<RoamState>(entity, new RoamState
            {
                currentWaypoint = Entity.Null,
                previousWaypoint = Entity.Null
            });

            AddComponent<ActionLock>(entity, new ActionLock
            {
                lockedAction = ActionType.None,
                isComplete = false,
                maxDuration = authoring.maxActionDuration,
                timer = 0f,
                stuckThreshold = authoring.stuckThreshold,
                stuckTime = authoring.stuckTime,
                stuckTimer = 0f,
                lastPosition = float3.zero
            });
            
            // Pending offers from interactables
            AddBuffer<PendingOffer>(entity);
        }
    }
}

public struct CitizenBrain : IComponentData { }