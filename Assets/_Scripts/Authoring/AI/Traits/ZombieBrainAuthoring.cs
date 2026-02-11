using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ZombieBrainAuthoring : MonoBehaviour
{
    [Header("Wander Settings")]
    public float wanderRadius = 15f;

    public class Baker : Baker<ZombieBrainAuthoring>
    {
        public override void Bake(ZombieBrainAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<ZombieBrain>(entity);

            // Zombies only wander (and attack, but that's separate)
            AddComponent<CanWander>(entity);

            // Zombies have simple needs
            AddComponent<Needs>(entity, new Needs
            {
                hunger = 0f,      // Always hungry (for brains!)
                energy = 1f,      // Never tired
                entertainment = 1f,
                social = 1f,
                comfort = 1f,
                bladder = 1f,
                safety = 1f,
                movement = 0.2f   // Always want to move
            });

            AddBuffer<ActionOption>(entity);
            AddComponent<ChosenActionOption>(entity);
            AddComponent<SelectedAction>(entity);
            AddComponent<CurrentInteraction>(entity);

            AddComponent<WanderState>(entity, new WanderState
            {
                wanderRadius = authoring.wanderRadius,
                wanderTarget = float3.zero
            });

            AddComponent<ActionLock>(entity, new ActionLock
            {
                lockedAction = ActionType.None,
                isComplete = false,
                maxDuration = 10f,
                timer = 0f,
                stuckThreshold = 0.5f,
                stuckTime = 2f,
                stuckTimer = 0f,
                lastPosition = float3.zero,
                decisionInterval = 0.5f,
                decisionTimer = 0f
            });
        }
    }
}

public struct ZombieBrain : IComponentData { }