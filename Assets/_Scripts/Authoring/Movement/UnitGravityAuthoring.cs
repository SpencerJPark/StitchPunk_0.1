using Unity.Entities;
using UnityEngine;

public class UnitGravityAuthoring : MonoBehaviour {

    public float fallSpeed;

    public class Baker : Baker<UnitGravityAuthoring> {
        public override void Bake(UnitGravityAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitGravity
            {
                fallSpeed = authoring.fallSpeed,
            });
        }
    }
}


