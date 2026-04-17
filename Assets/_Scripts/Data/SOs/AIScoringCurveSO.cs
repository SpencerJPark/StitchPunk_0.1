using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "ScoringCurve", menuName = "ScoringSO/Scoring Curve")]
public class AIScoringCurveSO : ScriptableObject
{
    [FormerlySerializedAs("motivationType")] public BehaviourType behaviourType;

    [Tooltip("X axis: need value (-100 to 100), Y axis: score output (-100 to 100)")]
    public AnimationCurve curve = AnimationCurve.Linear(-100, 100, 100, -100);
}