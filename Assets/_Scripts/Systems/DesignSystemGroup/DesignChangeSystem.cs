using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Runtime re-skin consumer. Reads ChangeDesignRequest (enabled-only): applies the palette shifts
// (shape tags + alternate-colour mode) to CharacterPalette and the explicit shape overrides to
// PersistedDesign so the new look persists, then re-derives EVERY design-driven slice + colour
// through the blobs and fans it to the child quads (a palette shift touches many parts at once —
// e.g. zombification flips every palette entry to its alternative variant), and disables the
// request (one-shot). Lives in DesignSystemGroup (after Health, before Animation) so a
// conversion-fired re-skin lands before the image-index push. Writes children on the main thread.
[BurstCompile]
[UpdateInGroup(typeof(DesignSystemGroup))]
public partial struct DesignChangeSystem : ISystem
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

        foreach (var (persistedDesign, palette, changeRequest, parts, entity) in
            SystemAPI.Query<RefRW<PersistedDesign>, RefRW<CharacterPalette>, RefRO<ChangeDesignRequest>,
                DynamicBuffer<BodyPart>>()
                .WithEntityAccess())
        {
            FixedList512Bytes<PaletteEntry> paletteChanges = changeRequest.ValueRO.paletteChanges;
            for (int changeIndex = 0; changeIndex < paletteChanges.Length; changeIndex++)
                DesignApplyUtil.SetTag(ref palette.ValueRW.groups, paletteChanges[changeIndex].group, paletteChanges[changeIndex].tag);

            FixedList128Bytes<ShapeOverride> shapeOverrides = changeRequest.ValueRO.shapeOverrides;
            for (int overrideIndex = 0; overrideIndex < shapeOverrides.Length; overrideIndex++)
                DesignApplyUtil.UpsertShape(ref persistedDesign.ValueRW.slots, shapeOverrides[overrideIndex].target, shapeOverrides[overrideIndex].shapeIndex);

            if (changeRequest.ValueRO.alternateColorMode == AlternateColorMode.Enable)
                palette.ValueRW.useAlternateColors = 1;
            else if (changeRequest.ValueRO.alternateColorMode == AlternateColorMode.Disable)
                palette.ValueRW.useAlternateColors = 0;

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

            SystemAPI.SetComponentEnabled<ChangeDesignRequest>(entity, false);
        }
    }
}
