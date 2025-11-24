using Unity.Entities;
using UnityEngine;

[CreateAssetMenu()]
public class BuildingResourceHarvesterTypeSO : BuildingTypeSO {

    public ResourceTypeSO.ResourceType harvestableResourceType;
    public float harvestDistance;
}