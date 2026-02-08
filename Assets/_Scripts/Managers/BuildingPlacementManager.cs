using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;

public class BuildingPlacementManager : Singleton<BuildingPlacementManager>, IUpdateObserver {
    
    public event EventHandler OnActiveBuildingTypeSOChanged;


    [SerializeField] private BuildingTypeSO buildingTypeSO;
    [SerializeField] private UnityEngine.Material ghostMaterial;


    private Transform ghostTransform;
    
    private void OnEnable() => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate() {
        if (ghostTransform != null) {
            ghostTransform.position = MouseWorldPosition.Instance.GetPosition();
        }

        if (EventSystem.current.IsPointerOverGameObject()) {
            return;
        }

        if (buildingTypeSO.IsNone()) {
            return;
        }

        if (Input.GetMouseButtonDown(1)) {
            SetActiveBuildingTypeSO(GameAssets.Instance.buildingTypeListSO.none);
        }

        if (Input.GetMouseButtonDown(0)) {
            
            if (!ResourceManager.Instance.CanSpendResourceAmount(buildingTypeSO.buildCostResourceAmountArray))
            {
                return;
            }
            
            if (CanPlaceBuilding()) {
                ResourceManager.Instance.SpendResourceAmount(buildingTypeSO.buildCostResourceAmountArray);
                
                Vector3 mouseWorldPosition = MouseWorldPosition.Instance.GetPosition();

                EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

                EntityQuery entityQuery = entityManager.CreateEntityQuery(typeof(StructureLibrary));
                StructureLibrary structureLibrary = entityQuery.GetSingleton<StructureLibrary>();

                //Entity spawnedEntity = entityManager.Instantiate(buildingTypeSO.GetPrefabEntity(entitiesReferences));
                //entityManager.SetComponentData(spawnedEntity, LocalTransform.FromPosition(mouseWorldPosition));
                
                Entity buildingConstructionVisualEntity = entityManager.Instantiate(buildingTypeSO.GetVisualPrefabEntity(structureLibrary));
                entityManager.SetComponentData(buildingConstructionVisualEntity, LocalTransform.FromPosition(mouseWorldPosition + new Vector3(0, buildingTypeSO.constructionYOffset, 0)));
                
                Entity buildingConstructionEntity = entityManager.Instantiate(structureLibrary.buildingConstructionPrefabEntity);
                entityManager.SetComponentData(buildingConstructionEntity, LocalTransform.FromPosition(mouseWorldPosition));
                entityManager.SetComponentData(buildingConstructionEntity, new BuildingConstruction
                {
                    buildingType = buildingTypeSO.buildingType,
                    constructionTimer = 0f,
                    constructionTimerMax = buildingTypeSO.buildingConstructionTimerMax,
                    finalPrefabEntity = buildingTypeSO.GetPrefabEntity(structureLibrary),
                    visualEntity = buildingConstructionVisualEntity,
                    startPosition = mouseWorldPosition + new Vector3(0, buildingTypeSO.constructionYOffset, 0),
                    endPosition = mouseWorldPosition,
                });

                DynamicBuffer<LinkedEntityGroup> linkedEntityGroupBuffer = entityManager.GetBuffer<LinkedEntityGroup>(buildingConstructionEntity);
                linkedEntityGroupBuffer.Add(new LinkedEntityGroup { Value = buildingConstructionVisualEntity }); 
            }
        }
    }

    private bool CanPlaceBuilding() {
        Vector3 mouseWorldPosition = MouseWorldPosition.Instance.GetPosition();
        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        EntityQuery entityQuery = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
        PhysicsWorldSingleton physicsWorldSingleton = entityQuery.GetSingleton<PhysicsWorldSingleton>();
        CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;
        CollisionFilter collisionFilter = new CollisionFilter {
            BelongsTo = ~0u,
            CollidesWith = 1u << GameAssets.STRUCTURES_LAYER | 1u << GameAssets.DEFAULT_LAYER,
            GroupIndex = 0,
        };

        UnityEngine.BoxCollider boxCollider = buildingTypeSO.prefab.GetComponent<UnityEngine.BoxCollider>();
        float bonusExtents = 1.1f;
        NativeList<DistanceHit> distanceHitList = new NativeList<DistanceHit>(Allocator.Temp);
        if (collisionWorld.OverlapBox(
            mouseWorldPosition,
            Quaternion.identity,
            boxCollider.size * .5f * bonusExtents,
            ref distanceHitList,
            collisionFilter)) {
            // Hit something
            return false;
        }

        distanceHitList.Clear();
        if (collisionWorld.OverlapSphere(
            mouseWorldPosition,
            buildingTypeSO.buildingDistanceMin,
            ref distanceHitList,
            collisionFilter)) {
            // Hit something within building radius
            foreach (DistanceHit distanceHit in distanceHitList) {
                if (entityManager.HasComponent<BuildingTypeSOHolder>(distanceHit.Entity)) {
                    BuildingTypeSOHolder buildingTypeSOHolder = entityManager.GetComponentData<BuildingTypeSOHolder>(distanceHit.Entity);
                    if (buildingTypeSOHolder.buildingType == buildingTypeSO.buildingType) {
                        // Same type too close
                        return false;
                    }
                }
                if (entityManager.HasComponent<BuildingConstruction>(distanceHit.Entity)) {
                    BuildingConstruction buildingConstruction = entityManager.GetComponentData<BuildingConstruction>(distanceHit.Entity);
                    if (buildingConstruction.buildingType == buildingTypeSO.buildingType) {
                        // Same type too close
                        return false;
                    }
                }
            }
        }

        if (buildingTypeSO is BuildingResourceHarvesterTypeSO buildingResourceHarvesterTypeSo)
        {
            bool hasValidResourceNodes = false;
            if (collisionWorld.OverlapSphere(
                    mouseWorldPosition,
                    buildingResourceHarvesterTypeSo.harvestDistance,
                    ref distanceHitList,
                    collisionFilter)) {
                // Hit something within Harvest Distance
                foreach (DistanceHit distanceHit in distanceHitList) {
                    if (entityManager.HasComponent<ResourceTypeSOHolder>(distanceHit.Entity)) {
                        ResourceTypeSOHolder resourceTypeSOHolder = entityManager.GetComponentData<ResourceTypeSOHolder>(distanceHit.Entity);
                        if (resourceTypeSOHolder.resourceType == buildingResourceHarvesterTypeSo.harvestableResourceType) {
                            // Nearby valid resource node
                            hasValidResourceNodes = true;
                            break;
                        }
                    }
                }
            }
            if (!hasValidResourceNodes)
            {
                return false;
            }
        }

        return true;
    }


    public BuildingTypeSO GetActiveBuildingTypeSO() {
        return buildingTypeSO;
    }

    public void SetActiveBuildingTypeSO(BuildingTypeSO buildingTypeSO) {
        this.buildingTypeSO = buildingTypeSO;

        if (ghostTransform != null) {
            Destroy(ghostTransform.gameObject);
        }

        if (!buildingTypeSO.IsNone()) {
            ghostTransform = Instantiate(buildingTypeSO.visualPrefab);
            foreach (MeshRenderer meshRenderer in ghostTransform.GetComponentsInChildren<MeshRenderer>()) {
                meshRenderer.material = ghostMaterial;
            }
        }

        OnActiveBuildingTypeSOChanged?.Invoke(this, EventArgs.Empty);
    }


}