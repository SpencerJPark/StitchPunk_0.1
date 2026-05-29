using UnityEngine;
using Unity.Mathematics;

[CreateAssetMenu(fileName = "AnimationSO", menuName = "Dots Animation/KeyframeSO")]
public class KeyframeSO: ScriptableObject
{
    public int frame;
    public float time;
    
    public float3 position;
    public float3 rotation;
    public float scale;
    
    public bool onFlipBookChange;
    public int2 matGridPosition;
}

public struct DOTSKeyframe
{
    public int frame;
    public float time;
    
    public float3 position;
    public float3 rotation;
    public float scale;
    
    public bool onFlipBookChange;
    public int2 matGridPosition;
}