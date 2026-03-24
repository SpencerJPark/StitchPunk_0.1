using Unity.Entities;
using UnityEngine;

public class GravityAuthoring : MonoBehaviour {

    public float fallSpeed;

    public class Baker : Baker<GravityAuthoring> {
        public override void Bake(GravityAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Gravity
            {
                fallSpeed = authoring.fallSpeed,
            });
        }
    }
}


