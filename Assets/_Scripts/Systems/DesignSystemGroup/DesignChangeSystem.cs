using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Runtime re-skin consumer. Reads ChangeDesignRequest (enabled-only): applies the palette shifts to
// CharacterPalette and the explicit shape overrides to PersistedDesign so the new look persists, then
// re-derives EVERY design-driven slice through the blob grid and fans it to the child quads (a palette
// shift touches many parts at once — e.g. zombification recolours all SkinColor-axis parts), and
// disables the request (one-shot). Lives in DesignSystemGroup (after Health, before Animation) so a
// conversion-fired re-skin lands before the image-index push. Writes children on the main thread.
[BurstCompile]
[UpdateInGroup(typeof(DesignSystemGroup))]
public partial struct DesignChangeSystem : ISystem
{
    private ComponentLookup<ImageIndex>              _imageIndexLookup;
    private ComponentLookup<AnimationTargetRestPose> _restPoseLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<PartLibrary>();
        _imageIndexLookup = state.GetComponentLookup<ImageIndex>(false);
        _restPoseLookup   = state.GetComponentLookup<AnimationTargetRestPose>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        PartLibrary library = SystemAPI.GetSingleton<PartLibrary>();
        if (!library.library.IsCreated)
            return;

        _imageIndexLookup.Update(ref state);
        _restPoseLookup.Update(ref state);

        // Main-thread ComponentLookup writes below conflict with UpdateImageIndexJob (reads ImageIndex)
        // still in flight from a prior frame — complete the input dependency before writing.
        state.CompleteDependency();

        foreach (var (persistedDesign, palette, changeRequest, parts, entity) in
            SystemAPI.Query<RefRW<PersistedDesign>, RefRW<CharacterPalette>, RefRO<ChangeDesignRequest>,
                DynamicBuffer<BodyPart>>()
                .WithEntityAccess())
        {
            FixedList512Bytes<PaletteEntry> paletteChanges = changeRequest.ValueRO.paletteChanges;
            for (int i = 0; i < paletteChanges.Length; i++)
                DesignApplyUtil.SetTag(ref palette.ValueRW.groups, paletteChanges[i].group, paletteChanges[i].tag);

            FixedList128Bytes<ShapeOverride> shapeOverrides = changeRequest.ValueRO.shapeOverrides;
            for (int i = 0; i < shapeOverrides.Length; i++)
                DesignApplyUtil.UpsertShape(ref persistedDesign.ValueRW.slots, shapeOverrides[i].target, shapeOverrides[i].shapeIndex);

            DesignApplyUtil.ApplyDesign(
                parts,
                persistedDesign.ValueRO.slots,
                palette.ValueRO,
                ref library.library.Value,
                ref _imageIndexLookup,
                ref _restPoseLookup);

            SystemAPI.SetComponentEnabled<ChangeDesignRequest>(entity, false);
        }
    }
}
