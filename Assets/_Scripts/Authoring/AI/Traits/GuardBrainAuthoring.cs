using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class GuardBrainAuthoring : MonoBehaviour
{
    [Header("Patrol Settings")]
    public float patrolRadius = 20f;

    [Header("Action Lock Settings")]
    public float maxActionDuration = 60f;
    public float decisionInterval = 0.5f;

    public class Baker : Baker<GuardBrainAuthoring>
    {
        public override void Bake(GuardBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<GuardBrain>(entity);

            // Guards have limited capabilities
            AddComponent<CanWander>(entity);  // Patrol
            AddComponent<CanEat>(entity);
            AddComponent<CanUseBathroom>(entity);
            // No CanSocialize, CanSleep during shift

            AddComponent<Needs>(entity, new Needs
            {
                hunger = 0.9f,
                energy = 0.9f,
                entertainment = 0.7f,
                social = 0.7f,
                comfort = 0.8f,
                bladder = 0.9f,
                safety = 1f,
                movement = 0.3f  // Guards need to move a lot
            });

            AddBuffer<ActionOption>(entity);
            AddComponent<ChosenActionOption>(entity);
            AddComponent<SelectedAction>(entity);
            AddComponent<CurrentInteraction>(entity);

            AddComponent<WanderState>(entity, new WanderState
            {
                wanderRadius = authoring.patrolRadius,
                wanderTarget = float3.zero
            });

            AddComponent<ActionLock>(entity, new ActionLock
            {
                lockedAction = ActionType.None,
                isComplete = false,
                maxDuration = authoring.maxActionDuration,
                timer = 0f,
                stuckThreshold = 1f,
                stuckTime = 3f,
                stuckTimer = 0f,
                lastPosition = float3.zero,
                decisionInterval = authoring.decisionInterval,
                decisionTimer = 0f
            });
        }
    }
}

public struct GuardBrain : IComponentData { }