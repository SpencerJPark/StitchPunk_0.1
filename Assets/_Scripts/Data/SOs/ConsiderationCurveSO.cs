using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "ConsiderationCurve", menuName = "AI/Scoring Curve")]
public class ConsiderationCurveSO : ScriptableObject
{
    [FormerlySerializedAs("motivationType")]
    public NeedType needType;

    [Tooltip("X axis: need value (-100 to 100), Y axis: score output (-100 to 100)")]
    public AnimationCurve curve = AnimationCurve.Linear(-100, 100, 100, -100);
}