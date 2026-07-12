using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Shared design resolve/apply helpers for the design-driven CharacterRig pipeline. Called from
// both DesignRandomizeSystem (spawn roll) and DesignApplySystem / DesignChangeSystem (apply + re-skin).
// Plain static methods — Burst-compiled as part of their bursted callers; never on a managed path.
//
// SHAPE model: a part belongs to a shape-tag `group`; the character stores one active `tag` per group
// in CharacterPalette (rolled from the rig authoring's RandomTagOption pool). A part's "tag pool" is
// the designs whose tag matches the character's tag for its group, followed by the part's
// tag-independent (empty-tag) designs. PersistedDesign stores the shape as an OFFSET into that pool,
// so swapping the tag (e.g. → "Zombie") keeps the shape.
//
// COLOUR model: the design that supplied the slice also supplies the colours — its 3 PartPaletteSlots
// feed _BaseColor/_SecondaryColor/_TertiaryColor. The character rolls ONE index per ColorPaletteType
// (full palette length, stored in CharacterPalette.colors — the palette type IS the sharing group);
// each slot clamps that index into its [min,max] window at apply, falling back to minColorIndex when
// unrolled. Every palette entry carries an `alternative` (its zombie/converted variant): shown when
// the slot sets useAlternateColor or the character is in alternate-colour mode (zombify).
public static class DesignApplyUtil
{
    // The active tag for a group on this character, or empty if the group is unset.
    public static FixedString32Bytes GetTag(in FixedList512Bytes<PaletteEntry> groups, in FixedString32Bytes group)
    {
        for (int entryIndex = 0; entryIndex < groups.Length; entryIndex++)
            if (groups[entryIndex].group == group)
                return groups[entryIndex].tag;
        return default;
    }

    // Upsert the active tag for a group.
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

    // The rolled colour index for a palette type on this character, or -1 if unset.
    public static int GetColorIndex(in FixedList64Bytes<ColorChoice> colors, ColorPaletteType palette)
    {
        for (int entryIndex = 0; entryIndex < colors.Length; entryIndex++)
            if (colors[entryIndex].palette == palette)
                return colors[entryIndex].index;
        return -1;
    }

    // Upsert the active colour index for a palette type. Capacity guard mirrors SetTag: dropping the
    // entry with a warning beats the FixedList throw this would otherwise become at runtime.
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

    // Number of slices a design contributes, honouring its stride (max 10, min 0, step 2 → 6 slices).
    private static int RangeCount(in PartDesignDef design)
    {
        if (design.maxTextureIndex < design.minTextureIndex) return 0;
        int step = design.step > 0 ? design.step : 1;
        return (design.maxTextureIndex - design.minTextureIndex) / step + 1;
    }

    // Number of slices in a part's tag pool: designs matching `tag`, plus (when `tag` is non-empty)
    // the tag-independent empty-tag designs. When `tag` is empty, only the empty-tag designs count
    // (they are already covered by the first loop), so the second loop is skipped to avoid double count.
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

    // Resolve an offset within a part's tag pool to a texture-array slice + the design that supplied
    // it (the matched design's palette slots colour the part). Returns -1 slice / -1 design if the
    // pool is empty. Offsets past the end clamp to the first matched design's first slice.
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

    // Collect the distinct palette types referenced by any design slot across all design-driven
    // parts — the set DesignRandomizeSystem rolls one colour index per. Slot [min,max] windows
    // narrow the shared roll at apply time, so a fixed-colour slot is simply a [n,n] window.
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

    // Resolve one design palette slot to a final linear-space colour. Returns false when the slot is
    // unused (palette None) or out of range — the caller skips the property write, leaving the baked
    // value. The character's rolled index for the palette (full palette length) clamps into the
    // slot's [min,max] window; an unrolled palette falls back to minColorIndex. The palette entry's
    // ALTERNATIVE colour (its zombie/converted variant) is used when the slot sets useAlternateColor
    // or the character is in alternate-colour mode.
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

    // Stored shape offset for a target, or 0 if the part was never rolled.
    public static int GetShapeIndex(in FixedList512Bytes<DesignSlot> slots, int target)
    {
        for (int entryIndex = 0; entryIndex < slots.Length; entryIndex++)
            if (slots[entryIndex].target == target)
                return slots[entryIndex].shapeIndex;
        return 0;
    }

    // Overwrite the shape offset for a target, or append it. Keeps PersistedDesign authoritative.
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

    // Re-derive and write every design-driven part's image + colours from the stored shape offsets +
    // the character's active tag per group + rolled colour per palette type. Writes
    // AnimationTargetRestPose.baseImageIndex (the no-animation source) and ImageIndex so
    // UpdateImageIndexSystem pushes _ImageIndex to the material, then the three per-instance tint
    // components (_BaseColor / _SecondaryColor / _TertiaryColor) from the MATCHED design's palette
    // slots. Unused slots (palette None) leave the baked tint untouched.
    public static void ApplyDesign(
        in DynamicBuffer<BodyPart> parts,
        in FixedList512Bytes<DesignSlot> slots,
        in CharacterPalette palette,
        ref PartLibraryBlob library,
        ref ColorPaletteLibraryBlob colorLibrary,
        ref ComponentLookup<ImageIndex> imageIndexLookup,
        ref ComponentLookup<AnimationTargetRestPose> restPoseLookup,
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

            if (slice >= 0)
            {
                if (restPoseLookup.HasComponent(child))
                {
                    AnimationTargetRestPose restPose = restPoseLookup[child];
                    restPose.baseImageIndex = slice;
                    restPoseLookup[child] = restPose;
                }

                if (imageIndexLookup.HasComponent(child))
                    imageIndexLookup[child] = new ImageIndex { index = slice, onUpdate = true };
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
