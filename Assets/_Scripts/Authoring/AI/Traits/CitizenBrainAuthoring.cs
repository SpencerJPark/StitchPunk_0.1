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
    public float decisionInterval = 0.2f;

    [Header("Needs (1 = satisfied, 0 = urgent)")]
    [Range(0f, 1f)] public float hunger = 0.8f;
    [Range(0f, 1f)] public float energy = 0.8f;
    [Range(0f, 1f)] public float entertainment = 0.6f;
    [Range(0f, 1f)] public float social = 0.5f;
    [Range(0f, 1f)] public float comfort = 0.8f;
    [Range(0f, 1f)] public float bladder = 0.9f;
    [Range(0f, 1f)] public float safety = 1f;
    [Range(0f, 1f)] public float movement = 0.5f;

    public class Baker : Baker<CitizenBrainAuthoring>
    {
        public override void Bake(CitizenBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            // Brain type tag
            AddComponent<CitizenBrain>(entity);

            // Capability tags (what this brain can do)
            AddComponent<CanEat>(entity);
            AddComponent<CanSleep>(entity);
            AddComponent<CanSocialize>(entity);
            AddComponent<CanWander>(entity);
            AddComponent<CanWork>(entity);
            AddComponent<CanSit>(entity);
            AddComponent<CanUseBathroom>(entity);

            // Needs
            AddComponent<Needs>(entity, new Needs
            {
                hunger = authoring.hunger,
                energy = authoring.energy,
                entertainment = authoring.entertainment,
                social = authoring.social,
                comfort = authoring.comfort,
                bladder = authoring.bladder,
                safety = authoring.safety,
                movement = authoring.movement
            });

            // Action selection
            AddBuffer<ActionOption>(entity);
            AddComponent<ChosenActionOption>(entity);
            AddComponent<SelectedAction>(entity);
            AddComponent<CurrentInteraction>(entity);

            // Wander state
            AddComponent<WanderState>(entity, new WanderState
            {
                wanderRadius = authoring.wanderRadius,
                wanderTarget = float3.zero
            });

            // Action lock with timeout/stuck detection
            AddComponent<ActionLock>(entity, new ActionLock
            {
                lockedAction = ActionType.None,
                isComplete = false,
                maxDuration = authoring.maxActionDuration,
                timer = 0f,
                stuckThreshold = authoring.stuckThreshold,
                stuckTime = authoring.stuckTime,
                stuckTimer = 0f,
                lastPosition = float3.zero,
                decisionInterval = authoring.decisionInterval,
                decisionTimer = 0f
            });
        }
    }
}

public struct CitizenBrain : IComponentData { }