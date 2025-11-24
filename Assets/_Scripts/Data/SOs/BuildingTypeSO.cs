using Unity.Entities;
using UnityEngine;

[CreateAssetMenu()]
public class BuildingTypeSO : ScriptableObject {


    public enum BuildingType {
        None,
        ZombieSpawner,
        Tower,
        Barracks,
        HQ,
        IronHarvester,
        GoldHarvester,
        OilHarvester,
    }


    public BuildingType buildingType;
    public float buildingConstructionTimerMax;
    public float constructionYOffset;
    public Transform prefab;
    public float buildingDistanceMin;
    public bool showInBuildingPlacementManagerUI;
    public Sprite sprite;
    public Transform visualPrefab;
    public ResourceAmount[] buildCostResourceAmountArray;


    public bool IsNone() {
        return buildingType == BuildingType.None;
    }

    public Entity GetPrefabEntity(EntitiesReferences entitiesReferences) {
        switch (buildingType) {
            default:
            case BuildingType.None:
            case BuildingType.Tower:    return entitiesReferences.buildingTowerPrefabEntity;
            case BuildingType.Barracks: return entitiesReferences.buildingBarracksPrefabEntity;
            case BuildingType.IronHarvester: return entitiesReferences.buildingIronHarvestorPrefabEntity;
            case BuildingType.GoldHarvester: return entitiesReferences.buildingGoldHarvestorPrefabEntity;
            case BuildingType.OilHarvester: return entitiesReferences.buildingOilHarvestorPrefabEntity;
        }
    }
    
    public Entity GetVisualPrefabEntity(EntitiesReferences entitiesReferences) {
        switch (buildingType) {
            default:
            case BuildingType.None:
            case BuildingType.Tower:    return entitiesReferences.buildingTowerVisualPrefabEntity;
            case BuildingType.Barracks: return entitiesReferences.buildingBarracksVisualPrefabEntity;
            case BuildingType.IronHarvester: return entitiesReferences.buildingIronHarvestorVisualPrefabEntity;
            case BuildingType.GoldHarvester: return entitiesReferences.buildingGoldHarvestorVisualPrefabEntity;
            case BuildingType.OilHarvester: return entitiesReferences.buildingOilHarvestorVisualPrefabEntity;
        }
    }

}