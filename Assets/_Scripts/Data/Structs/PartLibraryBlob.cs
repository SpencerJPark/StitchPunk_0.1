using Unity.Collections;
using Unity.Entities;

// Shared, immutable per-part DESIGN config, baked once into an enum-indexed blob by
// PartLibraryBakingSystem and read from Burst jobs via the PartLibrary singleton. Design-only:
// ragdoll/physics config lives on the joint empties (RagdollJointAuthoring → RagdollJointBakeData +
// RagdollLandingZone buffer), fully separate from this blob.
//
// A part belongs to a shape-tag `group` and lists DESIGNS. Each design bundles one tagged
// texture-slice span with its own palette slots, so shape and colour switch together: the character
// rolls one tag per group (CharacterPalette.groups, shared by every part in that group), the part's
// slice comes from the designs matching that tag (plus tag-independent empty-tag designs), and the
// matched design's slots say which palette colours feed _BaseColor/_SecondaryColor/_TertiaryColor.
// Entities carry only a UnitPartId index into `parts`.

// One palette reference on a design. The palette type is the colour sharing group — the character
// rolls ONE index per ColorPaletteType (full palette length); the slot clamps that index into its
// [minColorIndex, maxColorIndex] window at apply, so parts with the same window match exactly and a
// narrowed window stays as close as possible. An unrolled palette falls back to minColorIndex (the
// window start is the authored default). useAlternateColor statically shows the palette entry's
// alternative variant for this slot (the character-level alt mode does the same for every slot).
public struct PartPaletteSlot
{
    public ColorPaletteType palette;        // None = slot unused (apply skips the property write)
    public short            minColorIndex;  // window start — also the un-rolled default
    public short            maxColorIndex;  // window end (inclusive; clamped to the palette length)
    public bool             useAlternateColor; // always show the alternative variant (e.g. zombie skin)
}

// One look for a part: a tagged texture-slice span + the colours that go with it.
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

    // Direction alt-view offsets (DirectionFacing_System.md §4): frames to step from the rolled
    // design's rest slice for that facing. 0 everywhere = the part never changes art with facing
    // (it only mirrors, via PartFacing.mirrorX, if at all). No East slot — the roster default
    // (Six) never resolves to true profile; an Eight-count actor's true-profile parts read 0.
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
