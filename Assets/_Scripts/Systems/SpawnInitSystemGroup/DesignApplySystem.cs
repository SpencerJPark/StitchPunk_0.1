using Unity.Burst;
using Unity.Entities;

// Re-derives every design-driven part's slice from the rolled shapes (PersistedDesign) + palette
// (CharacterPalette) through the PartLibrary blob grid, and fans the results to the child quads on
// spawn. Runs after MinionRestoreApplySystem (so a restored minion's saved shapes/palette win over
// the fresh roll) and before SpawnInitCleanupSystem (which disables NewlySpawned). Writes child
// entities via ComponentLookup on the main thread — mirrors Ragdoll2DSpawnInitSystem. Never .Run().
[BurstCompile]
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
[UpdateAfter(typeof(MinionRestoreApplySystem))]
[UpdateBefore(typeof(SpawnInitCleanupSystem))]
public partial struct DesignApplySystem : ISystem
{
    private ComponentLookup<ImageIndex>              _imageIndexLookup;
    private ComponentLookup<AnimationTargetRestPose> _restPoseLookup;
    private ComponentLookup<BodyPartTint>            _baseTintLookup;
    private ComponentLookup<BodyPartSecondaryTint>   _secondaryTintLookup;
    private ComponentLookup<BodyPartTertiaryTint>    _tertiaryTintLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<PartLibrary>();
        state.RequireForUpdate<ColorPaletteLibrary>();
        _imageIndexLookup    = state.GetComponentLookup<ImageIndex>(false);
        _restPoseLookup      = state.GetComponentLookup<AnimationTargetRestPose>(false);
        _baseTintLookup      = state.GetComponentLookup<BodyPartTint>(false);
        _secondaryTintLookup = state.GetComponentLookup<BodyPartSecondaryTint>(false);
        _tertiaryTintLookup  = state.GetComponentLookup<BodyPartTertiaryTint>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PartLibrary library = SystemAPI.GetSingleton<PartLibrary>();
        ColorPaletteLibrary colorLibrary = SystemAPI.GetSingleton<ColorPaletteLibrary>();
        if (!library.library.IsCreated || !colorLibrary.blob.IsCreated)
            return;

        _imageIndexLookup.Update(ref state);
        _restPoseLookup.Update(ref state);
        _baseTintLookup.Update(ref state);
        _secondaryTintLookup.Update(ref state);
        _tertiaryTintLookup.Update(ref state);

        // Main-thread ComponentLookup writes below conflict with UpdateImageIndexJob (reads ImageIndex)
        // still in flight from a prior frame — complete the input dependency before writing.
        state.CompleteDependency();

        foreach (var (parts, persistedDesign, palette) in
            SystemAPI.Query<DynamicBuffer<BodyPart>, RefRO<PersistedDesign>, RefRO<CharacterPalette>>()
                .WithAll<NewlySpawned>())
        {
            DesignApplyUtil.ApplyDesign(
                parts,
                persistedDesign.ValueRO.slots,
                palette.ValueRO,
                ref library.library.Value,
                ref colorLibrary.blob.Value,
                ref _imageIndexLookup,
                ref _restPoseLookup,
                ref _baseTintLookup,
                ref _secondaryTintLookup,
                ref _tertiaryTintLookup);
        }
    }
}
