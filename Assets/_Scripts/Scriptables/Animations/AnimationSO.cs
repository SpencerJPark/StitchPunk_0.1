using UnityEngine;

[CreateAssetMenu(fileName = "AnimationSO", menuName = "Scriptable Objects/AnimationSO")]
public class AnimationSO : ScriptableObject
{
    public AnimationEnum animationEnum;
    public int totalFrames;
    public float speed;
    public KeyframeSO[] keyframes;
}
