// Global colour palette identity. One value per ColorPaletteSO asset; indexes the enum-indexed
// ColorPaletteLibraryBlob (Data-Blob-Pointer pattern, same shape as UnitPartId → PartLibraryBlob).
// Byte-backed so palette references ride 2-byte FixedList entries (ColorChoice on CharacterPalette).
// The palette type doubles as the colour SHARING group on a character: DesignRandomizeSystem rolls
// ONE colour index per type, so every design slot referencing e.g. Skin shows the same rolled tone
// (skin uniform across arms/face, hair across eyebrows/head); slots narrow the shared roll through
// their [min,max] window. Conversion looks (zombie skin, …) are NOT separate palette types — every
// palette entry carries an `alternative` colour (ColorVariation/ColorBlob) shown when the character
// is in alternate-colour mode or a slot sets useAlternateColor. Append-only, like every library enum.
public enum ColorPaletteType : byte
{
    None,        // slot unused — the apply pass skips the property write entirely
    World,
    Skin,
    Blood,
    Hair,
    Shirts,
}
