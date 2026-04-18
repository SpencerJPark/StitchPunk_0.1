using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Effect", menuName = "EffectSO/Effect")]
public class EffectSO : ScriptableObject
{
    [Tooltip("Effect Name")]
    [SearchableEnum] public EffectType effectType;

    public float effectTimer;

    [Header("Effect Behaviours")]
    [SearchableEnum] public BehaviourType[] behaviours;
}

// States are mearly different Behaviour swaps that happen when certain qualifiers are met and removed