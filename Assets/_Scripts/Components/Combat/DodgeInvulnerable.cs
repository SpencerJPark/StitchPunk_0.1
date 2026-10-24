using Unity.Entities;

// Enabled = this entity is mid-dodge and takes no damage. DamageEventSystem skips it before threat or health.
public struct DodgeInvulnerable : IComponentData, IEnableableComponent { }
