using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Stamps a freshly spawned unit's Health from UnitSO.maxHealth (via UnitLibrary), so health is
// authored in one place per unit type rather than per prefab. The prefab's HealthAuthoring numbers
// are a fallback for units placed directly in a scene, which never reach this pass.
//
// Runs before MinionRestoreApplySystem for the same reason DesignRandomizeSystem does: a restored
// minion's saved health must overwrite this stamp, not the other way round.
[BurstCompile]
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateBefore(typeof(MinionRestoreApplySystem))]
public partial struct UnitHealthInitSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<UnitDataLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        UnitDataLibrary library = SystemAPI.GetSingleton<UnitDataLibrary>();
        if (!library.library.IsCreated)
            return;

        state.Dependency = new UnitHealthInitJob
        {
            library = library.library,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(NewlySpawned))]
public partial struct UnitHealthInitJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob> library;

    public void Execute(in UnitData unitData, ref Health health)
    {
        int unitIndex = library.Value.FindByUnitType(unitData.unitType);
        if (unitIndex < 0)
            return;

        int maxHealth = library.Value.units[unitIndex].maxHealth;

        // A unit type nobody has given a health figure yet keeps whatever its prefab baked, rather
        // than spawning on zero and dying on the frame it appears.
        if (maxHealth <= 0)
            return;

        health.healthAmount = maxHealth;
        health.healthAmountMax = maxHealth;
    }
}
