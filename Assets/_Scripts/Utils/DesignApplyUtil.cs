using Unity.Collections;
using Unity.Entities;

// Shared apply/upsert helpers for the Unit Design System, called from both DesignApplySystem
// (spawn path) and DesignChangeSystem (runtime re-skin). Plain static methods — Burst-compiled
// as part of their bursted callers; never invoked on a managed path.
public static class DesignApplyUtil
{
    // Resolve `target` to its child quad through the root's AnimatorTarget buffer and write the
    // chosen image index. Writes AnimationTargetRestPose.baseImageIndex (the no-animation source)
    // and ImageIndex{index, onUpdate=true} so UpdateImageIndexSystem pushes _ImageIndex to the
    // material. Parts absent from the buffer (or missing the components) are skipped.
    public static void ApplySlot(
        in DynamicBuffer<AnimatorTarget> targets,
        int target,
        int imageIndex,
        ref ComponentLookup<ImageIndex> imageIndexLookup,
        ref ComponentLookup<AnimationTargetRestPose> restPoseLookup)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if ((int)targets[i].target != target)
                continue;

            Entity child = targets[i].entity;

            if (restPoseLookup.HasComponent(child))
            {
                AnimationTargetRestPose restPose = restPoseLookup[child];
                restPose.baseImageIndex = imageIndex;
                restPoseLookup[child] = restPose;
            }

            if (imageIndexLookup.HasComponent(child))
                imageIndexLookup[child] = new ImageIndex { index = imageIndex, onUpdate = true };
        }
    }

    // Overwrite the slot whose target matches, or append a new one. Keeps PersistedDesign
    // authoritative so the generic save pipeline captures the latest look.
    public static void UpsertSlot(ref FixedList512Bytes<DesignSlot> slots, int target, int imageIndex)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].target != target)
                continue;

            DesignSlot slot = slots[i];
            slot.imageIndex = imageIndex;
            slots[i] = slot;
            return;
        }

        slots.Add(new DesignSlot { target = target, imageIndex = imageIndex });
    }
}
