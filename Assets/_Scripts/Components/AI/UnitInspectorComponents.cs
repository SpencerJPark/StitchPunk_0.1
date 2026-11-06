using Unity.Entities;

public struct DebugWatchedUnit : IComponentData
{
    public Entity unit;
}

public enum DecisionCause : byte
{
    WinnerChanged = 0,
    Interrupted = 1,
    Fled = 2,
    Stuck = 3,
    PlayerOrder = 4,
    Finished = 5
}

[InternalBufferCapacity(0)]
public struct DecisionHistoryEntry : IBufferElementData
{
    public double worldSeconds;
    public ActionType fromAction;
    public ActionType toAction;
    public BehaviorType toBehavior;
    public int priority;
    public float winningUtility;
    public bool wasPlayerOrdered;
    public DecisionCause cause;
}
