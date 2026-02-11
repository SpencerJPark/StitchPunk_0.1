using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct AddInnateActionsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Single job that clears AND adds innate actions
        state.Dependency = new PrepareActionOptionsJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct PrepareActionOptionsJob : IJobEntity
{
    public void Execute(
        ref DynamicBuffer<ActionOption> options,
        in Needs needs,
        in ActionLock actionLock,
        [ChunkIndexInQuery] int chunkIndex)
    {
        bool needsDecision = actionLock.lockedAction == ActionType.None ||
                             actionLock.isComplete ||
                             actionLock.decisionTimer <= 0.05f;

        if (!needsDecision)
            return;

        // Clear
        options.Clear();

        // Add Idle (always available)
        float idleScore = 0.05f + needs.comfort * 0.05f;
        options.Add(new ActionOption
        {
            waypoint = Entity.Null,
            actionType = ActionType.Idle,
            animation = AnimationType.Idle,
            duration = 0f,
            needModifiers = new NeedModifiers { comfort = 0.005f },
            position = default,
            interactionRange = 0f,
            score = idleScore
        });
    }
}

// Separate system for Wander since it needs CanWander tag
[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(AddInnateActionsSystem))]
public partial struct AddWanderActionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new AddWanderActionJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(CanWander))]
public partial struct AddWanderActionJob : IJobEntity
{
    public void Execute(
        ref DynamicBuffer<ActionOption> options,
        in Needs needs,
        in ActionLock actionLock)
    {
        bool needsDecision = actionLock.lockedAction == ActionType.None ||
                             actionLock.isComplete ||
                             actionLock.decisionTimer <= 0.05f;

        if (!needsDecision)
            return;

        float wanderScore = (1f - needs.movement) * 0.5f + (1f - needs.entertainment) * 0.1f;
        options.Add(new ActionOption
        {
            waypoint = Entity.Null,
            actionType = ActionType.Wander,
            animation = AnimationType.Walk,
            duration = 0f,
            needModifiers = new NeedModifiers { movement = 0.02f, entertainment = 0.005f },
            position = default,
            interactionRange = 0f,
            score = wanderScore
        });
    }
}