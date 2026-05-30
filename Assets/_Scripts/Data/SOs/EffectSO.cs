using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Effect", menuName = "EffectSO/Effect")]
public class EffectSO : ScriptableObject
{
    [Tooltip("Effect Name")]
    [SearchableEnum] public EffectType effectType;
    public float value;
    public float timer;

    [Header("Effect Behaviours")]
    [SearchableEnum] public NeedType[] behaviours;
}

// States are mearly different Behaviour swaps that happen when certain qualifiers are met and removed