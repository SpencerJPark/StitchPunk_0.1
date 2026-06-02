using Unity.Entities;

// Runtime form of one BehaviorSO. Lives in the BehaviorLibrary blob, indexed by (int)behaviorType.
public struct BehaviorConfigBlob
{
    public BehaviorType behaviorType;
    public BlobArray<BehaviorCommand> executionSequence;
    public BlobArray<BehaviorCommand> interruptionCleanup;
}

// Enum-indexed library of behaviors. Systems read it directly, decoupled from action defs, e.g.
// library.behaviors[(int)BehaviorType.MeleeSwing].
public struct BehaviorLibraryBlob
{
    public BlobArray<BehaviorConfigBlob> behaviors;
}

public struct BehaviorCommand
{
    public BehaviorCommandType type;
    
    // Generic layout parameters used depending on the Type
    public int IntParam;      // Animation Hash, Faction ID, Dialogue Node ID
    public float FloatParam;  // Damage amount, Cash value, Dash speed
    public float Duration;    // How long this specific step takes before the next one runs
}

