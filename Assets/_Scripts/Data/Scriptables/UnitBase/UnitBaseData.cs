using UnityEngine;

[CreateAssetMenu(fileName = "UnitBaseData", menuName = "Units/Unit Base")]
public class UnitBaseData : ScriptableObject
{
    [field: SerializeField] public string UnitName { get; private set; }
    [field: SerializeField] public int MaxHealth { get; private set; }
    [field: SerializeField] public int AttackDamage { get; private set; }
    
    // Other references like effects and more
}

