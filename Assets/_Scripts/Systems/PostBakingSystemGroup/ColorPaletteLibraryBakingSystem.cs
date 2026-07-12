using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Bakes the ColorPaletteLibrarySO into an enum-indexed ColorPaletteLibraryBlob (Data-Blob-Pointer
// pattern), one ColorPaletteDef slot per ColorPaletteType. Mirrors PartLibraryBakingSystem. Every
// unauthored/empty slot gets a 1-entry white palette so a missing SO can never index out of range
// at runtime.
//
// ⚠ Colours are converted sRGB → LINEAR here (Color.linear): the DOTS MaterialProperty upload is
// raw — unlike the material inspector it does NOT auto-convert colour properties — and the project
// renders in Linear. Skipping this washes every palette colour out (same gotcha as
// BodyPartAuthoring's tintColor bake).
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct ColorPaletteLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ColorPaletteLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        ColorPaletteLibrarySO librarySO = null;
        foreach (RefRO<ColorPaletteLibraryReference> reference in SystemAPI.Query<RefRO<ColorPaletteLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        int paletteCount = BlobLibraryUtils.EnumCount<ColorPaletteType>();

        // Enum-keyed lookup with a duplicate-id warning (last-one-wins, like PartLibraryBaking).
        Dictionary<int, ColorPaletteSO> authoredPalettes = new Dictionary<int, ColorPaletteSO>();
        foreach (ColorPaletteSO paletteSO in librarySO.palettes)
        {
            if (paletteSO == null) continue;

            int slot = (int)paletteSO.paletteType;
            if (slot < 0 || slot >= paletteCount) continue;

            if (authoredPalettes.ContainsKey(slot))
                Debug.LogWarning(
                    $"[ColorPaletteLibraryBaking] Duplicate ColorPaletteType {paletteSO.paletteType} — " +
                    $"'{paletteSO.name}' overwrites an earlier SO with the same type (last-one-wins). " +
                    "Fix the library so each type is authored once.");
            authoredPalettes[slot] = paletteSO;
        }

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref ColorPaletteLibraryBlob root = ref builder.ConstructRoot<ColorPaletteLibraryBlob>();
        BlobBuilderArray<ColorPaletteDef> palettesBuilder = builder.Allocate(ref root.palettes, paletteCount);

        for (int slotIndex = 0; slotIndex < paletteCount; slotIndex++)
        {
            palettesBuilder[slotIndex].id = (ColorPaletteType)slotIndex;

            bool hasColors = authoredPalettes.TryGetValue(slotIndex, out ColorPaletteSO paletteSO)
                             && paletteSO.colors != null
                             && paletteSO.colors.Length > 0;

            if (!hasColors)
            {
                // Safe default: a 1-entry white palette (never indexes out of range, renders neutral).
                BlobBuilderArray<ColorBlob> fallbackColors = builder.Allocate(ref palettesBuilder[slotIndex].colors, 1);
                fallbackColors[0] = new ColorBlob
                {
                    color = new float4(1f, 1f, 1f, 1f),
                    alternative = new float4(1f, 1f, 1f, 1f)
                };
                continue;
            }

            BlobBuilderArray<ColorBlob> colorsBuilder =
                builder.Allocate(ref palettesBuilder[slotIndex].colors, paletteSO.colors.Length);
                
            for (int colorIndex = 0; colorIndex < paletteSO.colors.Length; colorIndex++)
            {
                Color linearColor = paletteSO.colors[colorIndex].color.linear;

                // No authored alternative → bake the main colour into both slots, so alternate mode
                // (zombify / slot useAlternateColor) simply keeps this entry unchanged at runtime.
                Color linearAlt = paletteSO.colors[colorIndex].hasAlternative
                    ? paletteSO.colors[colorIndex].alternative.linear
                    : linearColor;

                colorsBuilder[colorIndex] = new ColorBlob
                {
                    color = new float4(linearColor.r, linearColor.g, linearColor.b, linearColor.a),
                    alternative = new float4(linearAlt.r, linearAlt.g, linearAlt.b, linearAlt.a)
                };
            }
        }

        BlobAssetReference<ColorPaletteLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<ColorPaletteLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<ColorPaletteLibrary> holder in SystemAPI.Query<RefRW<ColorPaletteLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();

            holder.ValueRW.blob = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<ColorPaletteLibrary> holder in SystemAPI.Query<RefRW<ColorPaletteLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();
        }
    }
}