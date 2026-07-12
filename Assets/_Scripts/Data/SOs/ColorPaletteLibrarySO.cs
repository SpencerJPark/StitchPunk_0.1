using System.Collections.Generic;
using UnityEngine;

// The list of ColorPaletteSOs, one per ColorPaletteType, baked into ColorPaletteLibraryBlob by
// ColorPaletteLibraryBakingSystem. Mirrors PartLibrarySO. Drop the single _ColorPaletteLibrary.asset
// onto a ColorPaletteLibraryAuthoring GameObject in the scene.
[CreateAssetMenu(fileName = "_ColorPaletteLibrary", menuName = "Colors/Palette Library")]
public class ColorPaletteLibrarySO : ScriptableObject
{
    public List<ColorPaletteSO> palettes = new();

    public ColorPaletteSO Get(ColorPaletteType paletteType)
    {
        foreach (ColorPaletteSO palette in palettes)
        {
            if (palette != null && palette.paletteType == paletteType)
                return palette;
        }
        return null;
    }
}
