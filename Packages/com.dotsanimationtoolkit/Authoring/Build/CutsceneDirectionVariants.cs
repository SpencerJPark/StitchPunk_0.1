// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Reads a slot's direction set into the turn table a clip block bakes (amendment A65 §3.2).
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="CutsceneBlobBuilder"/> and the Cutscene Editor's preview, for the reason
    /// <see cref="CutsceneMarkMerge"/> is: the preview picks a variant from the asset and playback
    /// picks one from the blob, and if the two disagreed about which clips a set offers, the
    /// preview would show a turn that never happens.
    /// </remarks>
    internal static class CutsceneDirectionVariants
    {
        /// <summary>The five east-side slots a direction set authors, in the order the queue lists them.</summary>
        internal static readonly Direction[] EastSideSlotOrder =
        {
            Direction.South, Direction.SouthEast, Direction.East, Direction.NorthEast, Direction.North
        };

        /// <summary>
        /// Whether <paramref name="clipId"/> is one of the set's own five slots — the gate on
        /// substitution (decision A58-D5). A block naming the set's walk is asking for "the walk"
        /// and re-picks as the actor turns; a block naming a one-off clip the set has never heard of
        /// is asking for that clip exactly.
        /// </summary>
        internal static bool IsDirectionSetMember(DirectionSetAsset directionSet, ulong clipId)
        {
            if (directionSet == null || clipId == 0UL)
            {
                return false;
            }
            for (int slotIndex = 0; slotIndex < EastSideSlotOrder.Length; slotIndex++)
            {
                ClipAsset slotClip = directionSet.GetSlot(EastSideSlotOrder[slotIndex]);
                if (slotClip != null && slotClip.Id.Value == clipId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Builds the turn table for one block's clip, or leaves it empty when the block is not a member.</summary>
        internal static CutsceneDirectionVariantsBlob Build(CutsceneSlot slot, ulong clipId)
        {
            CutsceneDirectionVariantsBlob variants = default;
            if (slot == null || !IsDirectionSetMember(slot.directionSet, clipId))
            {
                return variants;
            }

            DirectionSetAsset directionSet = slot.directionSet;
            AnimationDirections effectiveDirections;
            directionSet.TryGetEffectiveDirections(out effectiveDirections);

            variants.hasVariants = true;
            variants.targetDirections = directionSet.targetDirections;
            variants.effectiveDirections = effectiveDirections;
            variants.south = SlotClipId(directionSet, Direction.South);
            variants.southEast = SlotClipId(directionSet, Direction.SouthEast);
            variants.east = SlotClipId(directionSet, Direction.East);
            variants.northEast = SlotClipId(directionSet, Direction.NorthEast);
            variants.north = SlotClipId(directionSet, Direction.North);
            return variants;
        }

        private static ulong SlotClipId(DirectionSetAsset directionSet, Direction eastSideFacing)
        {
            ClipAsset slotClip = directionSet.GetSlot(eastSideFacing);
            return slotClip != null ? slotClip.Id.Value : 0UL;
        }
    }
}
