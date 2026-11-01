using Unity.Entities;
using UnityEngine;

// Bullet prefab root. A projectile has no PoolOwner, so DespawnSystem destroys it on despawn (H5-D6; pooling is a follow-up).
public class ProjectileAuthoring : MonoBehaviour
{
    [Tooltip("Hit radius added to each unit's 0.4 m circle in the swept hit test.")]
    public float radius = 0.05f;

    public class Baker : Baker<ProjectileAuthoring>
    {
        public override void Bake(ProjectileAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Projectile { radius = authoring.radius });

            // AiShootSystem overwrites this with 1.5 x the gun's max range / speed.
            AddComponent(entity, new Lifetime { secondsRemaining = 1f });
            AddComponent(entity, new Despawn { mode = DespawnMode.Auto });
            SetComponentEnabled<Despawn>(entity, false);
        }
    }
}
