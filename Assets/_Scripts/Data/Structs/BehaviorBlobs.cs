using DotsAnimationToolkit;
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
    public int IntParam;      // Faction ID, Dialogue Node ID; LoopUntil: jump-back command index
    public float FloatParam;  // Damage amount, Cash value, Dash speed; LoopUntil: range for TargetOutOfRange
    public float Duration;    // How long this specific step takes before the next one runs; LoopUntil: loop timeout (0 = default)

    // Qualifier flags — exit conditions for LoopUntil; later also early-exit for blocking commands.
    public LoopQualifier Qualifier;
    public int           QualifierIntParam;   // MotivationSatisfied: (int)NeedType
    public float         QualifierFloatParam; // MotivationSatisfied: motivation threshold
    public bool          Looping;             // PlayAnimation: AnimationCommand loop mode (true = Loop, false = Once)

    // PlayAnimation only: the clip to play on the Action layer. Default (id 0) = invalid/none.
    public ClipId AnimationClip;

    // WaitForAnimEvent / WaitForClipFinished: the playback layer to watch (AnimationToolkitLayer,
    // 0 = Base, 1 = Action, ...).
    public byte LayerIndex;
}

