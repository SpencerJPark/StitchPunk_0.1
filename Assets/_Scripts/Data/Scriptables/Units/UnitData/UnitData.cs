using UnityEngine;

[CreateAssetMenu(fileName = "UnitData", menuName = "Units/Unit Data", order = 1)]
public class UnitData : ScriptableObject
{
    [field: SerializeField] public string UnitName { get; private set; }
    [field: SerializeField] public int MaxHealth { get; private set; }


    public UnitMovementData MovementData;

    [Header("Default State")]
    public UnitState DefaultState;
}
