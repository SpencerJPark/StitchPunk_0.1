using Unity.Entities;
using UnityEngine;

public class GravityAuthoring : MonoBehaviour {

    public float fallSpeed;
    [Tooltip("Distance from origin down to floor contact. 0 for foot-origin units. Set to half-height for center-origin meshes (e.g. 0.5 for a 1x1 cube).")]
    public float groundOffset = 0f;

    public class Baker : Baker<GravityAuthoring> {
        public override void Bake(GravityAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Gravity
            {
                fallSpeed    = authoring.fallSpeed,
                groundOffset = authoring.groundOffset,
            });
        }
    }
}


