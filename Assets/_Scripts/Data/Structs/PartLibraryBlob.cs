using Unity.Collections;
using Unity.Entities;

// Design-only per-part config, baked by PartLibraryBakingSystem; ragdoll config lives elsewhere (RagdollJointAuthoring).
// The character rolls one tag per group (CharacterPalette.groups); a part uses the designs matching that tag plus empty-tag designs.
// Entities carry only a UnitPartId index into `parts`.

// The character rolls one index per ColorPaletteType; the slot clamps it into [minColorIndex, maxColorIndex].
// An unrolled palette falls back to minColorIndex.
public struct PartPaletteSlot
{
    public ColorPaletteType palette;        // None = slot unused (apply skips the property write)
    public short            minColorIndex;  // window start — also the un-rolled default
    public short            maxColorIndex;  // window end (inclusive; clamped to the palette length)
    public bool             useAlternateColor; // always show the alternative variant (e.g. zombie skin)
}

public struct PartDesignDef
{
    public FixedString32Bytes tag;             // shape/colour label; empty = tag-independent ("any")
    public int                minTextureIndex; // inclusive first texture-array slice
    public int                maxTextureIndex; // inclusive last slice
    public int                step;            // stride between slices (1 = contiguous; 2 = interleaved L/R)

    public PartPaletteSlot primaryColor;    // → _BaseColor      (packed mask R, base fill)
    public PartPaletteSlot secondaryColor;  // → _SecondaryColor (packed mask G layer)
    public PartPaletteSlot tertiaryColor;   // → _TertiaryColor  (packed mask B layer)
}

public struct PartDef
{
    public UnitPartId id;
    public FixedString32Bytes group;        // shared shape-tag group this part follows ("Skin"/"Hair"/empty)
    public BlobArray<PartDesignDef> designs;

    // Frames to step from the rolled design's rest slice per facing; 0 = art never changes with facing.
    // No East slot: the default Six-direction roster never resolves to true profile.
    public int viewOffsetSouthEast;
    public int viewOffsetNorthEast;
    public int viewOffsetSouth;
    public int viewOffsetNorth;

    public int GetViewOffset(DotsAnimationToolkit.Direction eastSideFacing)
    {
        switch (eastSideFacing)
        {
            case DotsAnimationToolkit.Direction.SouthEast: return viewOffsetSouthEast;
            case DotsAnimationToolkit.Direction.NorthEast: return viewOffsetNorthEast;
            case DotsAnimationToolkit.Direction.South: return viewOffsetSouth;
            case DotsAnimationToolkit.Direction.North: return viewOffsetNorth;
            default: return 0;
        }
    }
}

public struct PartLibraryBlob
{
    public BlobArray<PartDef> parts;
}
