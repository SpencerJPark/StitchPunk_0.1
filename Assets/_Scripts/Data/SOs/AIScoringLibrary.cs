using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_ScoringLibrary", menuName = "ScoringSO/Scoring Library")]
public class AIScoringLibrarySO : ScriptableObject
{
    public List<AIScoringCurveSO> curves = new List<AIScoringCurveSO>();

    public AIScoringCurveSO GetCurve(BehaviourType type)
    {
        foreach (var curve in curves)
        {
            if (curve != null && curve.behaviourType == type)
                return curve;
        }
        return null;
    }
}