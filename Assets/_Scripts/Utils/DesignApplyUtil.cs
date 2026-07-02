using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Shared design resolve/apply helpers for the CharacterRig design pipeline. Called from both
// DesignApplySystem (spawn) and DesignChangeSystem (runtime re-skin). Plain static methods —
// Burst-compiled as part of their bursted callers; never invoked on a managed path.
public static class DesignApplyUtil
{
    // Colour column for a part's colour axis, read from the character-level palette.
    public static int ColorForAxis(PaletteGroup axis, in CharacterPalette palette)
    {
        switch (axis)
        {
            case PaletteGroup.SkinColor: return palette.skinColor;
            case PaletteGroup.HairColor: return palette.hairColor;
            default:                     return 0;
        }
    }

    // Resolve a (shape, color) pair to a texture-array slice through the part's grid. Both axes are
    // clamped to the part's declared counts so a palette value that exceeds a part's column set (or a
    // stale shape) can never index out of range.
    public static int ResolveSlice(ref PartDef def, int shapeIndex, int colorIndex)
    {
        int shapeCount = math.max(1, def.shapeCount);
        int colorCount = math.max(1, def.colorCount);
        int shape = math.clamp(shapeIndex, 0, shapeCount - 1);
        int color = math.clamp(colorIndex, 0, colorCount - 1);

        if (def.mode == GridMode.ExplicitTable)
        {
            int flat = shape * colorCount + color;
            if (flat >= 0 && flat < def.sliceTable.Length)
                return def.sliceTable[flat];
            return def.baseSlice;
        }

        return def.baseSlice + shape * colorCount + color;
    }

    // Stored shape for a target, or 0 if the part was never rolled.
    public static int GetShapeIndex(in FixedList512Bytes<DesignSlot> slots, int target)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].target == target)
                return slots[i].shapeIndex;
        return 0;
    }

    // Overwrite the shape for a target, or append it. Keeps PersistedDesign authoritative for saves.
    public static void UpsertShape(ref FixedList512Bytes<DesignSlot> slots, int target, int shapeIndex)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].target != target)
                continue;

            DesignSlot slot = slots[i];
            slot.shapeIndex = shapeIndex;
            slots[i] = slot;
            return;
        }

        slots.Add(new DesignSlot { target = target, shapeIndex = shapeIndex });
    }

    // Re-derive and write every design-driven part's image from the stored shapes + current palette.
    // Writes AnimationTargetRestPose.baseImageIndex (the no-animation source) and ImageIndex so
    // UpdateImageIndexSystem pushes _ImageIndex to the material. Used by both spawn apply and re-skin.
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
            int shapeIndex = GetShapeIndex(slots, (int)part.target);
            int colorIndex = ColorForAxis(def.colorAxis, palette);
            int slice = ResolveSlice(ref def, shapeIndex, colorIndex);

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
