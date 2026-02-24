using Unity.Entities;
using UnityEngine;

[CreateAssetMenu()]
public class UnitTypeSO : ScriptableObject {
    
    public enum UnitType {
        None,
        Soldier,
        Scout,
        Zombie,
    }


    public UnitType unitType;
    public Transform ragdollPreFab;
    public float progressMax;
    public Sprite sprite;
    public ResourceAmount[] SpawnCostResourceAmountArray;


    // public Entity GetPrefabEntity(StructureLibrary structureLibrary) {
    //     switch (unitType) {
    //         default:
    //         case UnitType.None:
    //         case UnitType.Soldier:  return structureLibrary.soldierPrefabEntity;
    //         case UnitType.Scout:    return structureLibrary.scoutPrefabEntity;
    //         case UnitType.Zombie:   return structureLibrary.zombiePrefabEntity;
    //     }
    // }


}