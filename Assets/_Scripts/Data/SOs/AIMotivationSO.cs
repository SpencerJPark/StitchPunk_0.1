using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_Motivation", menuName = "AI / Motivation")]
public class AIMotivationSO : ScriptableObject
{
    public List<AIConsiderationCurveSO> curves = new List<AIConsiderationCurveSO>();

    public AIConsiderationCurveSO GetCurve(MotivationType type)
    {
        foreach (var curve in curves)
        {
            if (curve != null && curve.motivationType == type)
                return curve;
        }
        return null;
    }
}