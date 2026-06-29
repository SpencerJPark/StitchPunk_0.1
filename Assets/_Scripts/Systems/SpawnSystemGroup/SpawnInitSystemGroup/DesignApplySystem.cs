using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Fans the chosen indices in PersistedDesign out to the child quads on spawn. Runs after
// MinionRestoreApplySystem (so a restored minion's saved indices win over the fresh roll) and before
// SpawnInitCleanupSystem (which disables NewlySpawned). Writes child entities via ComponentLookup on
// the main thread — mirrors Ragdoll2DSpawnInitSystem; spawn-init volume is low. Never .Run().
[BurstCompile]
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateAfter(typeof(MinionRestoreApplySystem))]
[UpdateBefore(typeof(SpawnInitCleanupSystem))]
public partial struct DesignApplySystem : ISystem
{
    private ComponentLookup<ImageIndex>             _imageIndexLookup;
    private ComponentLookup<AnimationTargetRestPose> _restPoseLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        _imageIndexLookup = state.GetComponentLookup<ImageIndex>(false);
        _restPoseLookup   = state.GetComponentLookup<AnimationTargetRestPose>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _imageIndexLookup.Update(ref state);
        _restPoseLookup.Update(ref state);

        // Main-thread ComponentLookup writes below conflict with UpdateImageIndexJob (reads ImageIndex)
        // still in flight from a prior frame — complete the input dependency before writing.
        state.CompleteDependency();

        foreach (var (persistedDesign, targets) in
            SystemAPI.Query<RefRO<PersistedDesign>, DynamicBuffer<AnimatorTarget>>()
                .WithAll<NewlySpawned>())
        {
            FixedList512Bytes<DesignSlot> slots = persistedDesign.ValueRO.slots;
            for (int i = 0; i < slots.Length; i++)
            {
                DesignApplyUtil.ApplySlot(
                    targets, slots[i].target, slots[i].imageIndex,
                    ref _imageIndexLookup, ref _restPoseLookup);
            }
        }
    }
}
