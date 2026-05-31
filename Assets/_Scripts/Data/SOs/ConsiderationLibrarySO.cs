using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_ConsiderationLibrary", menuName = "AI/Consideration Library")]
public class ConsiderationLibrarySO : ScriptableObject
{
    public List<ConsiderationCurveSO> curves = new List<ConsiderationCurveSO>();

    public ConsiderationCurveSO GetCurve(NeedType type)
    {
        foreach (var curve in curves)
        {
            if (curve != null && curve.needType == type)
                return curve;
        }
        return null;
    }
}