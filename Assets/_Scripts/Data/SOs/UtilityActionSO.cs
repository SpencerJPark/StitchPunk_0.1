using System.Collections.Generic;
using UnityEngine;

// One inline consideration on a UtilityActionSO. The curve is authored directly here (no separate
// ConsiderationCurveSO) and sampled into a blob at bake. X axis is the NORMALIZED [0,1] input
// (e.g. health ratio, distance/range ratio); Y is the per-consideration score factor.
[System.Serializable]
public class ConsiderationAuthoring
{
    [Tooltip("Which flattened ActionContext value feeds this curve")]
    public ConsiderationType input;

    [Tooltip("X: normalized input [0,1]; Y: score factor")]
    public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("How strongly this consideration contributes to the action's total utility")]
    public float weight = 1f;
}

// A choosable action: binds one BehaviorSO to its scoring considerations + selection metadata.
[CreateAssetMenu(fileName = "UtilityAction", menuName = "AI/Utility Action")]
public class UtilityActionSO : ScriptableObject
{
    [Tooltip("Drives animation linking and downstream request routing")]
    public ActionType actionType;

    [Tooltip("Higher-priority actions win the priority-tier gate during selection")]
    public int priority;

    [Tooltip("Self = runs on owner; SingleTarget = needs one target entity")]
    public TargetingMode targeting;

    [Tooltip("The executed command sequence this action runs")]
    public BehaviorSO behavior;

    [Tooltip("Inline considerations (each with its own curve) that produce this action's utility")]
    public List<ConsiderationAuthoring> considerations = new List<ConsiderationAuthoring>();
}
