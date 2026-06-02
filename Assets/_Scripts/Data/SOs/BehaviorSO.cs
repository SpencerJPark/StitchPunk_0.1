using System.Collections.Generic;
using UnityEngine;

// Authoring mirror of BehaviorCommand (StateMachineComponents.cs). A plain serializable class is
// needed because the runtime struct lives in a BlobArray and cannot be edited in the Inspector.
[System.Serializable]
public class BehaviorCommandAuthoring
{
    public BehaviorCommandType type;

    [Tooltip("Animation hash, faction id, dialogue node id, etc. (meaning depends on type)")]
    public int IntParam;

    [Tooltip("Damage amount, cash value, dash speed, etc. (meaning depends on type)")]
    public float FloatParam;

    [Tooltip("How long this step runs before the next one (seconds)")]
    public float Duration;
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
