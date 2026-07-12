using Unity.Entities;
using Unity.Mathematics;

// Shared, immutable colour palettes, baked once into an enum-indexed blob by
// ColorPaletteLibraryBakingSystem and read from Burst jobs via the ColorPaletteLibrary singleton
// (EntityLibraries.cs). One ColorPaletteDef slot per ColorPaletteType; every slot is seeded with a
// 1-entry white palette at bake so a missing SO can never index out of range. Colours are stored in
// LINEAR space (converted at bake) because the DOTS MaterialProperty upload is raw — see the note in
// ColorPaletteLibraryBakingSystem.

public struct ColorBlob
{
    public float4 color;
    public float4 alternative;
}

public struct ColorPaletteDef
{
    public ColorPaletteType id;
    public BlobArray<ColorBlob> colors;   // linear-space RGBA; alpha = layer blend strength on G/B layers
}

public struct ColorPaletteLibraryBlob
{
    public BlobArray<ColorPaletteDef> palettes;   // enum-indexed, one slot per ColorPaletteType
}
