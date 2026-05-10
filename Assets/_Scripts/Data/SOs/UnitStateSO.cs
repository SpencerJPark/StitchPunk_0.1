using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "UnitState", menuName = "Units/UnitState")]
public class UnitStateSO : ScriptableObject
{
    [Tooltip("UnitState Name")]
    [SearchableEnum] public UnitStateType stateType;

    public float stateTimer;

    [Header("UnitState Behaviours")]
    [SearchableEnum] public MotivationType[] behaviours;
}

// States are mearly different Behaviour swaps that happen when certain qualifiers are met and removed