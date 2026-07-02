using Unity.Entities;
using Unity.Mathematics;

// Shared, immutable per-part static config, baked once into an enum-indexed blob by
// PartLibraryBakingSystem and read from Burst jobs via the PartLibrary singleton. Mirrors
// ItemLibraryBlob's shape. Entities carry only a PartDefId index into `parts`.

public struct PartDef
{
    public PartDefId id;

    // Design grid — resolves a (shape, color) pair to a texture-array slice.
    public GridMode     mode;
    public int          baseSlice;    // StrideFormula anchor
    public int          shapeCount;   // number of shape variants (randomize picks [0, shapeCount))
    public int          colorCount;   // number of colour columns per shape
    public PaletteGroup colorAxis;    // which CharacterPalette value drives the colour column
    public BlobArray<int> sliceTable; // ExplicitTable mode: length shapeCount * colorCount

    // Ragdoll config for parts flagged RagdollJoint.
    public float             defaultSettleSpeed; // deg/s toward the landing angle (0 = fall back to 8)
    public BlobArray<float2> zones;              // landing zones (x = min, y = max), one picked at death
}

public struct PartLibraryBlob
{
    public BlobArray<PartDef> parts;
}
