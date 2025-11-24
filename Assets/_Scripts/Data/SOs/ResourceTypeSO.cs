using Unity.Entities;
using UnityEngine;

[CreateAssetMenu(fileName = "ResourceTypeSO", menuName = "Scriptable Objects/ResourceTypeSO")]
public class ResourceTypeSO : ScriptableObject {


    public enum ResourceType {
        None,
        Iron,
        Gold,
        Oil,
    }


    public ResourceType resourceType;
    public Sprite sprite;
    

    public bool IsNone() {
        return resourceType == ResourceType.None;
    }

    // public Entity GetPrefabEntity(EntitiesReferences entitiesReferences) {
    //     switch (resourceType) {
    //         default:
    //         case ResourceType.None:
    //         case ResourceType.Tower:    return entitiesReferences.resourceTowerPrefabEntity;
    //         case ResourceType.Barracks: return entitiesReferences.resourceBarracksPrefabEntity;
    //     }
    // }

}