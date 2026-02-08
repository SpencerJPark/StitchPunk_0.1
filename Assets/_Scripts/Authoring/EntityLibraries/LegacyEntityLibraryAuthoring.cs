using Unity.Entities;
using UnityEngine;

public class LegacyEntityLibraryAuthoring : MonoBehaviour {


    public GameObject bulletPrefabGameObject;
    public GameObject zombiePrefabGameObject;
    public GameObject shootLightPrefabGameObject;
    public GameObject scoutPrefabGameObject;
    public GameObject soldierPrefabGameObject;

    public GameObject buildingTowerPrefabGameObject;
    public GameObject buildingBarracksPrefabGameObject;
    public GameObject buildingIronHarvestorPrefabGameObject;
    public GameObject buildingGoldHarvestorPrefabGameObject;
    public GameObject buildingOilHarvestorPrefabGameObject;
    
    public GameObject buildingTowerVisualPrefabGameObject;
    public GameObject buildingBarracksVisualPrefabGameObject;
    public GameObject buildingIronHarvestorVisualPrefabGameObject;
    public GameObject buildingGoldHarvestorVisualPrefabGameObject;
    public GameObject buildingOilHarvestorVisualPrefabGameObject;
    
    public GameObject buildingConstructionPrefabGameObject;


    public class Baker : Baker<LegacyEntityLibraryAuthoring> {


        public override void Bake(LegacyEntityLibraryAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new StructureLibrary {
                bulletPrefabEntity = GetEntity(authoring.bulletPrefabGameObject, TransformUsageFlags.Dynamic),
                zombiePrefabEntity = GetEntity(authoring.zombiePrefabGameObject, TransformUsageFlags.Dynamic),
                shootLightPrefabEntity = GetEntity(authoring.shootLightPrefabGameObject, TransformUsageFlags.Dynamic),
                scoutPrefabEntity = GetEntity(authoring.scoutPrefabGameObject, TransformUsageFlags.Dynamic),
                soldierPrefabEntity = GetEntity(authoring.soldierPrefabGameObject, TransformUsageFlags.Dynamic),
                
                buildingTowerPrefabEntity = GetEntity(authoring.buildingTowerPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingBarracksPrefabEntity = GetEntity(authoring.buildingBarracksPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingIronHarvestorPrefabEntity = GetEntity(authoring.buildingIronHarvestorPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingGoldHarvestorPrefabEntity = GetEntity(authoring.buildingGoldHarvestorPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingOilHarvestorPrefabEntity = GetEntity(authoring.buildingOilHarvestorPrefabGameObject, TransformUsageFlags.Dynamic),
                
                buildingTowerVisualPrefabEntity = GetEntity(authoring.buildingTowerVisualPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingBarracksVisualPrefabEntity = GetEntity(authoring.buildingBarracksVisualPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingIronHarvestorVisualPrefabEntity = GetEntity(authoring.buildingIronHarvestorVisualPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingGoldHarvestorVisualPrefabEntity = GetEntity(authoring.buildingGoldHarvestorVisualPrefabGameObject, TransformUsageFlags.Dynamic),
                buildingOilHarvestorVisualPrefabEntity = GetEntity(authoring.buildingOilHarvestorVisualPrefabGameObject, TransformUsageFlags.Dynamic),
                
                buildingConstructionPrefabEntity = GetEntity(authoring.buildingConstructionPrefabGameObject, TransformUsageFlags.Dynamic),
            });
        }

    }

}


public struct StructureLibrary : IComponentData {

    public Entity bulletPrefabEntity;
    public Entity zombiePrefabEntity;
    public Entity shootLightPrefabEntity;
    public Entity scoutPrefabEntity;
    public Entity soldierPrefabEntity;

    public Entity buildingTowerPrefabEntity;
    public Entity buildingBarracksPrefabEntity;
    public Entity buildingIronHarvestorPrefabEntity;
    public Entity buildingGoldHarvestorPrefabEntity;
    public Entity buildingOilHarvestorPrefabEntity;
    
    public Entity buildingTowerVisualPrefabEntity;
    public Entity buildingBarracksVisualPrefabEntity;
    public Entity buildingIronHarvestorVisualPrefabEntity;
    public Entity buildingGoldHarvestorVisualPrefabEntity;
    public Entity buildingOilHarvestorVisualPrefabEntity;
    
    public Entity buildingConstructionPrefabEntity;

}