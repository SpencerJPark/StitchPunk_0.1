using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AISystemGroup))]
[UpdateBefore(typeof(AIAwarenessSystemGroup))]
public partial struct NeedsDecaySystem : ISystem
{
    private ComponentLookup<UnitMover> moverLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        moverLookup = state.GetComponentLookup<UnitMover>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        moverLookup.Update(ref state);
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new NeedsDecayJob
        {
            deltaTime = deltaTime,
            moverLookup = moverLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct NeedsDecayJob : IJobEntity
{
    public float deltaTime;
    [ReadOnly] public ComponentLookup<UnitMover> moverLookup;

    public void Execute(ref Needs needs, in BrainLink brainLink)
    {
        // All needs decay toward 0 (urgent) over time
        float hungerDecay = 0.001f;
        float energyDecay = 0.0008f;
        float entertainmentDecay = 0.002f;
        float socialDecay = 0.001f;
        float comfortDecay = 0.0015f;
        float bladderDecay = 0.0012f;
        float movementDecay = 0.003f;

        // Safety recovers toward 1 naturally
        float safetyRecovery = 0.005f;

        needs.hunger = math.saturate(needs.hunger - hungerDecay * deltaTime);
        needs.energy = math.saturate(needs.energy - energyDecay * deltaTime);
        needs.entertainment = math.saturate(needs.entertainment - entertainmentDecay * deltaTime);
        needs.social = math.saturate(needs.social - socialDecay * deltaTime);
        needs.comfort = math.saturate(needs.comfort - comfortDecay * deltaTime);
        needs.bladder = math.saturate(needs.bladder - bladderDecay * deltaTime);
        needs.safety = math.saturate(needs.safety + safetyRecovery * deltaTime);

        // Movement - decays when stationary, recovers when moving
        if (moverLookup.TryGetComponent(brainLink.body, out UnitMover mover))
        {
            if (mover.isMoving)
            {
                needs.movement = math.saturate(needs.movement + 0.01f * deltaTime);
            }
            else
            {
                needs.movement = math.saturate(needs.movement - movementDecay * deltaTime);
            }
        }
        else
        {
            needs.movement = math.saturate(needs.movement - movementDecay * deltaTime);
        }
    }
}