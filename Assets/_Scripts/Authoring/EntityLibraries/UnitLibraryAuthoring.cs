using Unity.Entities;
using UnityEngine;

public class UnitLibraryAuthoring : MonoBehaviour {

    public UnitTypeSO.UnitType unitType;
    
    public class Baker : Baker<UnitLibraryAuthoring> {
        public override void Bake(UnitLibraryAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitTypeHolder {
                unitType = authoring.unitType
            });
        }
    }

}

public struct UnitTypeHolder : IComponentData {
    public UnitTypeSO.UnitType unitType;
}