using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Consideration", menuName = "AI / Consideration")]
public class AIConsiderationCurveSO : ScriptableObject
{
    public MotivationType motivationType;

    [Tooltip("X axis: need value (-100 to 100), Y axis: score output (-100 to 100)")]
    public AnimationCurve curve = AnimationCurve.Linear(-100, 100, 100, -100);
}