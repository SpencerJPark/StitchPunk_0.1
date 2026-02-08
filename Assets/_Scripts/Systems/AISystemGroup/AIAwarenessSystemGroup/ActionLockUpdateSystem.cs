using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AISystemGroup))]
[UpdateBefore(typeof(AISelectionSystemGroup))]
public partial struct ActionLockUpdateSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;

    public void OnCreate(ref SystemState state)
    {
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new ActionLockUpdateJob
        {
            deltaTime = deltaTime,
            transformLookup = transformLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ActionLockUpdateJob : IJobEntity
{
    public float deltaTime;
    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;

    public void Execute(
        ref ActionLock actionLock,
        in BrainLink brainLink)
    {
        if (actionLock.lockedAction == ActionType.None)
            return;

        // Update timer
        actionLock.timer += deltaTime;

        // Timeout check
        if (actionLock.timer >= actionLock.maxDuration)
        {
            actionLock.isComplete = true;
            return;
        }

        // Stuck detection
        if (!transformLookup.TryGetComponent(brainLink.body, out LocalTransform bodyTransform))
            return;

        float3 currentPos = bodyTransform.Position;
        float distMoved = math.distance(currentPos, actionLock.lastPosition);

        if (distMoved < actionLock.stuckThreshold * deltaTime)
        {
            actionLock.stuckTimer += deltaTime;

            if (actionLock.stuckTimer >= actionLock.stuckTime)
            {
                // Stuck too long, release lock
                actionLock.isComplete = true;
            }
        }
        else
        {
            // Moving, reset stuck timer
            actionLock.stuckTimer = 0f;
        }

        actionLock.lastPosition = currentPos;
    }
}