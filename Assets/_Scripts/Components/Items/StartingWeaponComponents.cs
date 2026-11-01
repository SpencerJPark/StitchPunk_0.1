using Unity.Entities;

// Per-instance starting weapon (SmartHumanAI H5-D10), so one bandit in a camp can be the gunman. Enabled at bake;
// StartingWeaponSpawnSystem equips it and disables this, and re-arms on NewlySpawned (pool reuse).
public struct StartingWeapon : IComponentData, IEnableableComponent
{
    public Entity weaponPrefab;
}
