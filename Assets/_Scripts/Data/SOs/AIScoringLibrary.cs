using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_ScoringLibrary", menuName = "AI/Scoring Library")]
public class AIScoringLibrarySO : ScriptableObject
{
    public List<AIScoringCurveSO> curves = new List<AIScoringCurveSO>();

    public AIScoringCurveSO GetCurve(NeedType type)
    {
        foreach (var curve in curves)
        {
            if (curve != null && curve.needType == type)
                return curve;
        }
        return null;
    }
}