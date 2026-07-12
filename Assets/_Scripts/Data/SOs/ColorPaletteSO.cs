using UnityEngine;
using System;

// One colour palette — the authoritative list of colours for one ColorPaletteType (every skin tone,
// every hair colour, …). Part slots reference palettes by the enum only; changing or adding colours
// here re-themes everything that points at the palette. Baked into the enum-indexed
// ColorPaletteLibraryBlob by ColorPaletteLibraryBakingSystem; systems read the blob via the
// ColorPaletteLibrary singleton, never this SO. Colour ALPHA is meaningful on secondary/tertiary
// part slots: PackedChannelRecolor uses it as the layer blend strength (0 hides the layer, 1 shows
// it fully); the base layer ignores alpha.
[CreateAssetMenu(fileName = "Color Palette", menuName = "Colors/Palette")]
public class ColorPaletteSO : ScriptableObject
{
    [SearchableEnum] public ColorPaletteType paletteType;

    public ColorVariation[] colors;
}

[Serializable]
public struct ColorVariation
{
    public Color color;

    [Tooltip("This entry has a distinct alternative (zombie/state or shade variant). Unchecked = " +
             "alternate mode just shows the main color.")]
    public bool hasAlternative;

    [ShowWhen("hasAlternative")]
    public Color alternative;
}