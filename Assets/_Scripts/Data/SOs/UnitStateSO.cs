using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "UnitState", menuName = "UnitStateSO/UnitState")]
public class UnitStateSO : ScriptableObject
{
    [Tooltip("UnitState Name")]
    [SearchableEnum] public UnitStateType stateType;

    public float stateTimer;

    [Header("UnitState Behaviours")]
    [SearchableEnum] public BehaviourType[] behaviours;
}

// States are mearly different Behaviour swaps that happen when certain qualifiers are met and removed