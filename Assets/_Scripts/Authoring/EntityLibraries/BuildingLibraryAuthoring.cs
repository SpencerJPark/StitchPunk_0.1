// using Unity.Entities;
// using UnityEngine;
//
// public class BuildingLibraryAuthoring : MonoBehaviour {
//
//
//     public BuildingTypeSO.BuildingType buildingType;
//
//
//     public class Baker : Baker<BuildingLibraryAuthoring> {
//
//
//         public override void Bake(BuildingLibraryAuthoring authoring) {
//             Entity entity = GetEntity(TransformUsageFlags.Dynamic);
//             AddComponent(entity, new BuildingTypeSOHolder {
//                 buildingType = authoring.buildingType,
//             });
//         }
//     }
//
// }
//
//
//
// public struct BuildingTypeSOHolder : IComponentData {
//
//
//     public BuildingTypeSO.BuildingType buildingType;
//
//
// }