using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

// Authoring mirror of BehaviorCommand (StateMachineComponents.cs). A plain serializable class is
// needed because the runtime struct lives in a BlobArray and cannot be edited in the Inspector.
[System.Serializable]
public class BehaviorCommandAuthoring
{
    public BehaviorCommandType type;

    [Tooltip("Faction id, dialogue node id, etc. (meaning depends on type). LoopUntil: jump-back command index.")]
    public int IntParam;

    [Tooltip("Damage amount, cash value, dash speed, etc. (meaning depends on type). LoopUntil: range for TargetOutOfRange.")]
    public float FloatParam;

    [Tooltip("How long this step runs before the next one (seconds). LoopUntil: loop timeout (0 = default 60s)")]
    public float Duration;

    [Tooltip("LoopUntil exit conditions — exit when ANY ticked flag is true")]
    public LoopQualifier Qualifier;

    [Tooltip("Qualifier param: (int)NeedType for MotivationSatisfied")]
    public int QualifierIntParam;

    [Tooltip("Qualifier param: motivation threshold for MotivationSatisfied")]
    public float QualifierFloatParam;

    [Tooltip("PlayAnimation: loop the clip until stopped (StopAnimation or interrupt cleanup)")]
    public bool Looping;

    [Tooltip("PlayAnimation: the clip to play on the Action layer.")]
    public ClipAsset AnimationClip;
}

// A reusable executed sequence ("verb") — e.g. MeleeSwing, Wander. Bound to a UtilityActionSO and
// baked into the enum-indexed BehaviorLibrary blob, keyed by behaviorType.
[CreateAssetMenu(fileName = "Behavior", menuName = "AI/Behavior")]
public class BehaviorSO : ScriptableObject
{
    [Tooltip("Stable key into the BehaviorLibrary blob. Must be unique across all behaviors.")]
    public BehaviorType behaviorType;

    [Tooltip("The step-by-step command sequence. Use an Approach command to navigate — set FloatParam = stopping distance, IntParam = StanceType (0=walk, 2=run).")]
    public List<BehaviorCommandAuthoring> executionSequence = new List<BehaviorCommandAuthoring>();

    [Tooltip("Run ONLY if the execution sequence is forcefully canceled (interrupt)")]
    public List<BehaviorCommandAuthoring> interruptionCleanup = new List<BehaviorCommandAuthoring>();
}
