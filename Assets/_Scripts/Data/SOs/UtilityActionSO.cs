using System.Collections.Generic;
using UnityEngine;

// One inline consideration on a UtilityActionSO. The curve is authored directly here (no separate
// sampled into a blob at bake. X axis is the NORMALIZED [0,1] input
// (e.g. health ratio, distance/range ratio); Y is the per-consideration score factor.
[System.Serializable]
public class ConsiderationAuthoring
{
    [Tooltip("Which value feeds this curve (Health, Motivation, DistanceToTarget, Trait, ...)")]
    public ConsiderationType input;

    [Tooltip("Which motivation to sample when input == Motivation")]
    public NeedType needType;

    [Tooltip("Which personality trait to sample when input == Trait")]
    public PersonalityTypes traitType;

    [Tooltip("X: normalized input [0,1]; Y: score factor")]
    public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("Importance 0–1: 1 applies the full curve, 0 ignores this consideration (score = lerp(1, curve, weight))")]
    public float weight = 1f;
}

// A choosable action: binds one BehaviorSO to its scoring considerations + selection metadata.
[CreateAssetMenu(fileName = "UtilityAction", menuName = "AI/Utility Action")]
public class UtilityActionSO : ScriptableObject
{
    [Tooltip("Drives animation linking and downstream request routing")]
    public ActionType actionType;

    [Tooltip("Priority tier (see AIPriorityTier): only a higher tier can preempt a live behaviour. Brains may override it per ActionType.")]
    public int priority;

    [Tooltip("The executed command sequence this action runs")]
    public BehaviorSO behavior;

    [Tooltip("Inline considerations (each with its own curve) that produce this action's utility")]
    public List<ConsiderationAuthoring> considerations = new List<ConsiderationAuthoring>();
}
