using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "_UnitLibrary", menuName = "Units/Unit Library")]
public class UnitLibrarySO : ScriptableObject {
    
    public List<UnitSO> units;
    
    public UnitSO GetUnitSO(UnitType unitType) {
        foreach (UnitSO unitSO in units) {
            if (unitSO.unitType == unitType) {
                return unitSO;
            }
        }
        Debug.LogError("Could not find UnitTypeSO for UnitType " + unitType);
        return null;
    }
}