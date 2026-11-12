using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Shape: a part's tag pool is designs matching the character's tag for its group, then empty-tag
// designs. PersistedDesign stores an offset into that pool, so a tag swap keeps the shape.
// Colour: the matched design's 3 palette slots supply the colours; each clamps the character's one
// rolled index per ColorPaletteType into its [min,max] window (minColorIndex when unrolled).
public static class DesignApplyUtil
{
    public static FixedString32Bytes GetTag(in FixedList512Bytes<PaletteEntry> groups, in FixedString32Bytes group)
    {
        for (int entryIndex = 0; entryIndex < groups.Length; entryIndex++)
            if (groups[entryIndex].group == group)
                return groups[entryIndex].tag;
        return default;
    }

    public static void SetTag(ref FixedList512Bytes<PaletteEntry> groups, in FixedString32Bytes group, in FixedString32Bytes tag)
    {
        for (int entryIndex = 0; entryIndex < groups.Length; entryIndex++)
        {
            if (groups[entryIndex].group != group)
                continue;

            PaletteEntry entry = groups[entryIndex];
            entry.tag = tag;
            groups[entryIndex] = entry;
            return;
        }

        if (groups.Length >= groups.Capacity)
        {
            // See the ceiling note on CharacterPalette.groups (~7 entries). Dropping the tag with a
            // warning beats the FixedList throw this would otherwise become at runtime.
            UnityEngine.Debug.LogWarning(
                (FixedString128Bytes)"[CharacterPalette] palette group capacity reached — new group dropped (raise groups size).");
            return;
        }

        groups.Add(new PaletteEntry { group = group, tag = tag });
    }

    public static int GetColorIndex(in FixedList64Bytes<ColorChoice> colors, ColorPaletteType palette)
    {
        for (int entryIndex = 0; entryIndex < colors.Length; entryIndex++)
            if (colors[entryIndex].palette == palette)
                return colors[entryIndex].index;
        return -1;
    }

    // Capacity guard mirrors SetTag: warn and drop instead of a FixedList throw.
    public static void SetColorIndex(ref FixedList64Bytes<ColorChoice> colors, ColorPaletteType palette, byte index)
    {
        for (int entryIndex = 0; entryIndex < colors.Length; entryIndex++)
        {
            if (colors[entryIndex].palette != palette)
                continue;

            ColorChoice choice = colors[entryIndex];
            choice.index = index;
            colors[entryIndex] = choice;
            return;
        }

        if (colors.Length >= colors.Capacity)
        {
            UnityEngine.Debug.LogWarning(
                (FixedString128Bytes)"[CharacterPalette] colour choice capacity reached — new entry dropped (raise colors size).");
            return;
        }

        colors.Add(new ColorChoice { palette = palette, index = index });
    }

    // Stride counts: max 10, min 0, step 2 gives 6 slices.
    private static int RangeCount(in PartDesignDef design)
    {
        if (design.maxTextureIndex < design.minTextureIndex) return 0;
        int step = design.step > 0 ? design.step : 1;
        return (design.maxTextureIndex - design.minTextureIndex) / step + 1;
    }

    // Empty `tag` skips the second loop: empty-tag designs are already counted by the first.
    public static int TagPoolSize(ref PartDef def, in FixedString32Bytes tag)
    {
        int total = 0;
        for (int designIndex = 0; designIndex < def.designs.Length; designIndex++)
            if (def.designs[designIndex].tag == tag)
                total += RangeCount(def.designs[designIndex]);

        if (tag.Length != 0)
        {
            for (int designIndex = 0; designIndex < def.designs.Length; designIndex++)
                if (def.designs[designIndex].tag.Length == 0)
                    total += RangeCount(def.designs[designIndex]);
        }
        return total;
    }

    // Returns -1 slice / -1 design if the pool is empty; offsets past the end clamp to the first slice.
    public static int SliceAtOffset(ref PartDef def, in FixedString32Bytes tag, int offset, out int matchedDesignIndex)
    {
        int remaining = offset < 0 ? 0 : offset;
        int fallbackSlice = -1;
        int fallbackDesign = -1;

        for (int designIndex = 0; designIndex < def.designs.Length; designIndex++)
        {
            if (def.designs[designIndex].tag != tag) continue;
            int count = RangeCount(def.designs[designIndex]);
            if (count <= 0) continue;
            int step = def.designs[designIndex].step > 0 ? def.designs[designIndex].step : 1;
            if (fallbackSlice < 0)
            {
                fallbackSlice  = def.designs[designIndex].minTextureIndex;
                fallbackDesign = designIndex;
            }
            if (remaining < count)
            {
                matchedDesignIndex = designIndex;
                return def.designs[designIndex].minTextureIndex + remaining * step;
            }
            remaining -= count;
        }

        if (tag.Length != 0)
        {
            for (int designIndex = 0; designIndex < def.designs.Length; designIndex++)
            {
                if (def.designs[designIndex].tag.Length != 0) continue;
                int count = RangeCount(def.designs[designIndex]);
                if (count <= 0) continue;
                int step = def.designs[designIndex].step > 0 ? def.designs[designIndex].step : 1;
                if (fallbackSlice < 0)
                {
                    fallbackSlice  = def.designs[designIndex].minTextureIndex;
                    fallbackDesign = designIndex;
                }
                if (remaining < count)
                {
                    matchedDesignIndex = designIndex;
                    return def.designs[designIndex].minTextureIndex + remaining * step;
                }
                remaining -= count;
            }
        }

        matchedDesignIndex = fallbackDesign;
        return fallbackSlice;
    }

    // The set DesignRandomizeSystem rolls one colour index per; a fixed-colour slot is a [n,n] window.
    public static void CollectPalettes(
        in DynamicBuffer<BodyPart> parts,
        ref PartLibraryBlob library,
        ref FixedList64Bytes<ColorPaletteType> outPalettes)
    {
        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            if ((parts[partIndex].flags & BodyPartFlags.DesignSlot) == 0) continue;
            int defIndex = (int)parts[partIndex].unitPart;
            if (defIndex < 0 || defIndex >= library.parts.Length) continue;

            ref PartDef def = ref library.parts[defIndex];
            for (int designIndex = 0; designIndex < def.designs.Length; designIndex++)
            {
                AddPalette(def.designs[designIndex].primaryColor.palette, ref outPalettes);
                AddPalette(def.designs[designIndex].secondaryColor.palette, ref outPalettes);
                AddPalette(def.designs[designIndex].tertiaryColor.palette, ref outPalettes);
            }
        }
    }

    private static void AddPalette(ColorPaletteType palette, ref FixedList64Bytes<ColorPaletteType> outPalettes)
    {
        if (palette == ColorPaletteType.None)
            return;
        for (int entryIndex = 0; entryIndex < outPalettes.Length; entryIndex++)
            if (outPalettes[entryIndex] == palette)
                return;
        if (outPalettes.Length < outPalettes.Capacity)
            outPalettes.Add(palette);
    }

    // False when the slot is unused or out of range: the caller skips the write, keeping the baked value.
    // The alternative (zombie) colour is used when the slot sets useAlternateColor or the character is in alternate mode.
    public static bool ResolveColor(
        ref ColorPaletteLibraryBlob paletteLibrary,
        in CharacterPalette palette,
        in PartPaletteSlot slot,
        out float4 color)
    {
        color = new float4(1f, 1f, 1f, 1f);
        if (slot.palette == ColorPaletteType.None)
            return false;

        int paletteIndex = (int)slot.palette;
        if (paletteIndex <= 0 || paletteIndex >= paletteLibrary.palettes.Length)
            return false;

        ref ColorPaletteDef paletteDef = ref paletteLibrary.palettes[paletteIndex];
        if (paletteDef.colors.Length == 0)
            return false;

        int windowMin = math.clamp(slot.minColorIndex, 0, paletteDef.colors.Length - 1);
        int windowMax = math.clamp(math.max((int)slot.maxColorIndex, windowMin), 0, paletteDef.colors.Length - 1);

        int colorIndex = GetColorIndex(palette.colors, slot.palette);
        if (colorIndex < 0)
            colorIndex = slot.minColorIndex;
        colorIndex = math.clamp(colorIndex, windowMin, windowMax);

        bool useAlternate = slot.useAlternateColor || palette.useAlternateColors != 0;
        color = useAlternate
            ? paletteDef.colors[colorIndex].alternative
            : paletteDef.colors[colorIndex].color;
        return true;
    }

    public static int GetShapeIndex(in FixedList512Bytes<DesignSlot> slots, int target)
    {
        for (int entryIndex = 0; entryIndex < slots.Length; entryIndex++)
            if (slots[entryIndex].target == target)
                return slots[entryIndex].shapeIndex;
        return 0;
    }

    public static void UpsertShape(ref FixedList512Bytes<DesignSlot> slots, int target, int shapeIndex)
    {
        for (int entryIndex = 0; entryIndex < slots.Length; entryIndex++)
        {
            if (slots[entryIndex].target != target) continue;

            DesignSlot slot = slots[entryIndex];
            slot.shapeIndex = shapeIndex;
            slots[entryIndex] = slot;
            return;
        }

        slots.Add(new DesignSlot { target = target, shapeIndex = shapeIndex });
    }

    // Writes TargetRestPose.restSliceIndex so RelativeToRest sprite tracks retarget to the rolled variant.
    // Unused slots (palette None) leave the baked tint untouched.
    public static void ApplyDesign(
        in DynamicBuffer<BodyPart> parts,
        in FixedList512Bytes<DesignSlot> slots,
        in CharacterPalette palette,
        ref PartLibraryBlob library,
        ref ColorPaletteLibraryBlob colorLibrary,
        ref ComponentLookup<TargetRestPose> restPoseLookup,
        ref ComponentLookup<BodyPartTint> baseTintLookup,
        ref ComponentLookup<BodyPartSecondaryTint> secondaryTintLookup,
        ref ComponentLookup<BodyPartTertiaryTint> tertiaryTintLookup)
    {
        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            BodyPart part = parts[partIndex];
            if ((part.flags & BodyPartFlags.DesignSlot) == 0)
                continue;

            int defIndex = (int)part.unitPart;
            if (defIndex < 0 || defIndex >= library.parts.Length)
                continue;

            ref PartDef def = ref library.parts[defIndex];
            FixedString32Bytes tag = GetTag(palette.groups, def.group);
            int offset = GetShapeIndex(slots, (int)part.target);
            int slice = SliceAtOffset(ref def, tag, offset, out int matchedDesignIndex);

            Entity child = part.entity;

            if (slice >= 0 && restPoseLookup.HasComponent(child))
            {
                TargetRestPose restPose = restPoseLookup[child];
                restPose.restSliceIndex = slice;
                restPoseLookup[child] = restPose;
            }

            // Colour axis: the matched design's slots. No matched design (empty pool) = no colours.
            if (matchedDesignIndex < 0 || matchedDesignIndex >= def.designs.Length)
                continue;

            ref PartDesignDef design = ref def.designs[matchedDesignIndex];

            if (baseTintLookup.HasComponent(child)
                && ResolveColor(ref colorLibrary, palette, design.primaryColor, out float4 primaryColor))
                baseTintLookup[child] = new BodyPartTint { Value = primaryColor };

            if (secondaryTintLookup.HasComponent(child)
                && ResolveColor(ref colorLibrary, palette, design.secondaryColor, out float4 secondaryColor))
                secondaryTintLookup[child] = new BodyPartSecondaryTint { Value = secondaryColor };

            if (tertiaryTintLookup.HasComponent(child)
                && ResolveColor(ref colorLibrary, palette, design.tertiaryColor, out float4 tertiaryColor))
                tertiaryTintLookup[child] = new BodyPartTertiaryTint { Value = tertiaryColor };
        }
    }
}
