// using Unity.Entities;
// using UnityEngine;
//
// [CreateAssetMenu()]
// public class BuildingTypeSO : ScriptableObject {
//
//
//     public enum BuildingType {
//         None,
//         ZombieSpawner,
//         Tower,
//         Barracks,
//         HQ,
//         IronHarvester,
//         GoldHarvester,
//         OilHarvester,
//     }
//
//
//     public BuildingType buildingType;
//     public float buildingConstructionTimerMax;
//     public float constructionYOffset;
//     public Transform prefab;
//     public float buildingDistanceMin;
//     public bool showInBuildingPlacementManagerUI;
//     public Sprite sprite;
//     public Transform visualPrefab;
//     // public ResourceAmount[] buildCostResourceAmountArray;
//
//
//     public bool IsNone() {
//         return buildingType == BuildingType.None;
//     }
//
//     // public Entity GetPrefabEntity(StructureLibrary structureLibrary) {
//     //     switch (buildingType) {
//     //         default:
//     //         case BuildingType.None:
//     //         case BuildingType.Tower:    return structureLibrary.buildingTowerPrefabEntity;
//     //         case BuildingType.Barracks: return structureLibrary.buildingBarracksPrefabEntity;
//     //         case BuildingType.IronHarvester: return structureLibrary.buildingIronHarvestorPrefabEntity;
//     //         case BuildingType.GoldHarvester: return structureLibrary.buildingGoldHarvestorPrefabEntity;
//     //         case BuildingType.OilHarvester: return structureLibrary.buildingOilHarvestorPrefabEntity;
//     //     }
//     // }
//     //
//     // public Entity GetVisualPrefabEntity(StructureLibrary structureLibrary) {
//     //     switch (buildingType) {
//     //         default:
//     //         case BuildingType.None:
//     //         case BuildingType.Tower:    return structureLibrary.buildingTowerVisualPrefabEntity;
//     //         case BuildingType.Barracks: return structureLibrary.buildingBarracksVisualPrefabEntity;
//     //         case BuildingType.IronHarvester: return structureLibrary.buildingIronHarvestorVisualPrefabEntity;
//     //         case BuildingType.GoldHarvester: return structureLibrary.buildingGoldHarvestorVisualPrefabEntity;
//     //         case BuildingType.OilHarvester: return structureLibrary.buildingOilHarvestorVisualPrefabEntity;
//     //     }
//     // }
//
// }