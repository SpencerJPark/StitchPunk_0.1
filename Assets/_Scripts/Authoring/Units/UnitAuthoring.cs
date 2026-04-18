using Unity.Entities;
using UnityEngine;

public class UnitAuthoring : MonoBehaviour {

    public UnitType unitType;

    public class Baker : Baker<UnitAuthoring> {

        public override void Bake(UnitAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Unit());
            AddComponent(entity, new UnitData { unitType = authoring.unitType });
            AddComponent(entity, new UnitAction());
            
            AddComponent<Target>(entity);
            SetComponentEnabled<Target>(entity, false);
            AddComponent<MoveToTargetRequest>(entity);
            SetComponentEnabled<MoveToTargetRequest>(entity, false);
            AddComponent<ArrivedAtTarget>(entity);
            SetComponentEnabled<ArrivedAtTarget>(entity, false);
            
            AddComponent<NewlySpawned>(entity);
            SetComponentEnabled<NewlySpawned>(entity, false);
        }
    }
}

