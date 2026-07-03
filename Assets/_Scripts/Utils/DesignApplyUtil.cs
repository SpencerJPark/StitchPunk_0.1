using Unity.Collections;
using Unity.Entities;

// Shared design resolve/apply helpers for the tag-driven CharacterRig design pipeline. Called from
// both DesignRandomizeSystem (spawn roll) and DesignApplySystem / DesignChangeSystem (apply + re-skin).
// Plain static methods — Burst-compiled as part of their bursted callers; never on a managed path.
//
// Model: a part belongs to a palette `group`; the character stores one active `tag` per group in
// CharacterPalette. A part's "tag pool" is the ranges whose tag matches the character's tag for its
// group, followed by the part's colour-independent (empty-tag) ranges. PersistedDesign stores the
// shape as an OFFSET into that pool, so swapping the tag (e.g. → "Zombie") keeps the shape.
public static class DesignApplyUtil
{
    // The active tag for a group on this character, or empty if the group is unset.
    public static FixedString32Bytes GetTag(in FixedList512Bytes<PaletteEntry> groups, in FixedString32Bytes group)
    {
        for (int i = 0; i < groups.Length; i++)
            if (groups[i].group == group)
                return groups[i].tag;
        return default;
    }

    // Upsert the active tag for a group.
    public static void SetTag(ref FixedList512Bytes<PaletteEntry> groups, in FixedString32Bytes group, in FixedString32Bytes tag)
    {
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i].group != group)
                continue;

            PaletteEntry entry = groups[i];
            entry.tag = tag;
            groups[i] = entry;
            return;
        }

        if (groups.Length >= groups.Capacity)
        {
            // See the ceiling note on CharacterPalette.groups (~7 entries). Dropping the tag with a
            // warning beats the FixedList throw this would otherwise become at runtime.
            UnityEngine.Debug.LogWarning(
                (Unity.Collections.FixedString128Bytes)"[CharacterPalette] palette group capacity reached — new group dropped (raise groups size).");
            return;
        }

        groups.Add(new PaletteEntry { group = group, tag = tag });
    }

    // Number of slices a range contributes, honouring its stride. (max 10, min 0, step 2 → 6 slices).
    private static int RangeCount(in PartTagRange range)
    {
        if (range.max < range.min) return 0;
        int step = range.step > 0 ? range.step : 1;
        return (range.max - range.min) / step + 1;
    }

    // Number of slices in a part's tag pool: ranges matching `tag`, plus (when `tag` is non-empty)
    // the colour-independent empty-tag ranges. When `tag` is empty, only the empty-tag ranges count
    // (they are already covered by the first loop), so the second loop is skipped to avoid double count.
    public static int TagPoolSize(ref PartDef def, in FixedString32Bytes tag)
    {
        int total = 0;
        for (int i = 0; i < def.ranges.Length; i++)
            if (def.ranges[i].tag == tag)
                total += RangeCount(def.ranges[i]);

        if (tag.Length != 0)
        {
            for (int i = 0; i < def.ranges.Length; i++)
                if (def.ranges[i].tag.Length == 0)
                    total += RangeCount(def.ranges[i]);
        }
        return total;
    }

    // Resolve an offset within a part's tag pool to a texture-array slice, honouring each range's
    // stride. Returns -1 if the pool is empty. Offsets past the end clamp to the first matched slice.
    public static int SliceAtOffset(ref PartDef def, in FixedString32Bytes tag, int offset)
    {
        int remaining = offset < 0 ? 0 : offset;
        int fallback = -1;

        for (int i = 0; i < def.ranges.Length; i++)
        {
            if (def.ranges[i].tag != tag) continue;
            int count = RangeCount(def.ranges[i]);
            if (count <= 0) continue;
            int step = def.ranges[i].step > 0 ? def.ranges[i].step : 1;
            if (fallback < 0) fallback = def.ranges[i].min;
            if (remaining < count) return def.ranges[i].min + remaining * step;
            remaining -= count;
        }

        if (tag.Length != 0)
        {
            for (int i = 0; i < def.ranges.Length; i++)
            {
                if (def.ranges[i].tag.Length != 0) continue;
                int count = RangeCount(def.ranges[i]);
                if (count <= 0) continue;
                int step = def.ranges[i].step > 0 ? def.ranges[i].step : 1;
                if (fallback < 0) fallback = def.ranges[i].min;
                if (remaining < count) return def.ranges[i].min + remaining * step;
                remaining -= count;
            }
        }

        return fallback;
    }

    // Collect the distinct non-empty group names across all design-driven parts.
    public static void CollectGroups(
        in DynamicBuffer<BodyPart> parts,
        ref PartLibraryBlob library,
        ref FixedList512Bytes<FixedString32Bytes> outGroups)
    {
        for (int p = 0; p < parts.Length; p++)
        {
            if ((parts[p].flags & BodyPartFlags.DesignSlot) == 0) continue;
            int defIndex = (int)parts[p].partDef;
            if (defIndex < 0 || defIndex >= library.parts.Length) continue;

            FixedString32Bytes group = library.parts[defIndex].group;
            if (group.Length == 0) continue;
            if (!Contains(outGroups, group)) outGroups.Add(group);
        }
    }

    // Collect the distinct randomizable tags available for one group across all design-driven parts.
    public static void CollectRandomTags(
        in DynamicBuffer<BodyPart> parts,
        in FixedString32Bytes group,
        ref PartLibraryBlob library,
        ref FixedList512Bytes<FixedString32Bytes> outTags)
    {
        for (int p = 0; p < parts.Length; p++)
        {
            if ((parts[p].flags & BodyPartFlags.DesignSlot) == 0) continue;
            int defIndex = (int)parts[p].partDef;
            if (defIndex < 0 || defIndex >= library.parts.Length) continue;

            ref PartDef def = ref library.parts[defIndex];
            if (def.group != group) continue;

            for (int i = 0; i < def.ranges.Length; i++)
            {
                if (!def.ranges[i].randomizable) continue;
                if (def.ranges[i].tag.Length == 0) continue;
                if (!Contains(outTags, def.ranges[i].tag)) outTags.Add(def.ranges[i].tag);
            }
        }
    }

    private static bool Contains(in FixedList512Bytes<FixedString32Bytes> list, in FixedString32Bytes value)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] == value) return true;
        return false;
    }

    // Stored shape offset for a target, or 0 if the part was never rolled.
    public static int GetShapeIndex(in FixedList512Bytes<DesignSlot> slots, int target)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].target == target)
                return slots[i].shapeIndex;
        return 0;
    }

    // Overwrite the shape offset for a target, or append it. Keeps PersistedDesign authoritative.
    public static void UpsertShape(ref FixedList512Bytes<DesignSlot> slots, int target, int shapeIndex)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].target != target) continue;

            DesignSlot slot = slots[i];
            slot.shapeIndex = shapeIndex;
            slots[i] = slot;
            return;
        }

        slots.Add(new DesignSlot { target = target, shapeIndex = shapeIndex });
    }

    // Re-derive and write every design-driven part's image from the stored shape offsets + the
    // character's active tag per group. Writes AnimationTargetRestPose.baseImageIndex (the no-animation
    // source) and ImageIndex so UpdateImageIndexSystem pushes _ImageIndex to the material.
    public static void ApplyDesign(
        in DynamicBuffer<BodyPart> parts,
        in FixedList512Bytes<DesignSlot> slots,
        in CharacterPalette palette,
        ref PartLibraryBlob library,
        ref ComponentLookup<ImageIndex> imageIndexLookup,
        ref ComponentLookup<AnimationTargetRestPose> restPoseLookup)
    {
        for (int i = 0; i < parts.Length; i++)
        {
            BodyPart part = parts[i];
            if ((part.flags & BodyPartFlags.DesignSlot) == 0)
                continue;

            int defIndex = (int)part.partDef;
            if (defIndex < 0 || defIndex >= library.parts.Length)
                continue;

            ref PartDef def = ref library.parts[defIndex];
            FixedString32Bytes tag = GetTag(palette.groups, def.group);
            int offset = GetShapeIndex(slots, (int)part.target);
            int slice = SliceAtOffset(ref def, tag, offset);
            if (slice < 0)
                continue;

            Entity child = part.entity;

            if (restPoseLookup.HasComponent(child))
            {
                AnimationTargetRestPose restPose = restPoseLookup[child];
                restPose.baseImageIndex = slice;
                restPoseLookup[child] = restPose;
            }

            if (imageIndexLookup.HasComponent(child))
                imageIndexLookup[child] = new ImageIndex { index = slice, onUpdate = true };
        }
    }
}
