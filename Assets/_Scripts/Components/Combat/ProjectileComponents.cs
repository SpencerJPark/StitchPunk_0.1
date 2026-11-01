using Unity.Entities;
using Unity.Mathematics;

// A bullet in flight (SmartHumanAI H5-D6). ProjectileFlightSystem moves it and runs the shared swept hit test
// (ProjectileHitUtils); a hit or its Lifetime running out enables Despawn. Rocks stay on the ThrownItem path.
public struct Projectile : IComponentData
{
    public float3       velocity;
    public Entity       shooter;
    public float3       origin;
    public DamageSource damageSource;
    public float        radius;
}
