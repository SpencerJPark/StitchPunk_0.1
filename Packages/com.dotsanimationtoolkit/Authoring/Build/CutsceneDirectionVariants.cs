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

        /// <summary>
        /// Whether a slot's rig can actually show the facing the cutscene resolves for it, and what
        /// is wrong when it cannot. Null means nothing is wrong.
        /// </summary>
        /// <remarks>
        /// <strong>Nothing opted in</strong> is the failure that turns nothing: <c>facesDirection</c>
        /// is what bakes <c>PartFacing</c> and what gates the mirror, so a rig where no target sets
        /// it resolves a facing, picks a variant, and moves nothing — it cost an owner checkpoint on
        /// 2026-09-06. <strong>A flag under another flag</strong> is merely redundant: a mirror point
        /// reflects its whole subtree, and <c>PartMirrorFromAncestor</c> makes the inner flag be
        /// ignored rather than cancel the outer one. Reported so an author can tidy it, not because
        /// it breaks anything.
        /// </remarks>
        internal static string DescribeFacingRigProblem(CutsceneSlot slot)
        {
            if (slot == null || slot.kind != CutsceneSlotKind.Actor
                || slot.directionSet == null || slot.rig == null || slot.rig.targets == null)
            {
                return null;
            }

            int facingTargetCount = 0;
            string redundantFlagNote = null;
            for (int targetIndex = 0; targetIndex < slot.rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = slot.rig.targets[targetIndex];
                if (target == null || !target.facesDirection)
                {
                    continue;
                }
                facingTargetCount++;

                if (redundantFlagNote == null)
                {
                    RigTargetDefinition mirroredAncestor = FindMirroredAncestor(slot.rig, target);
                    if (mirroredAncestor != null)
                    {
                        redundantFlagNote = "'" + DescribeTarget(target) + "' has Faces Direction set "
                            + "inside '" + DescribeTarget(mirroredAncestor) + "', which is already a "
                            + "mirror point and reflects it. The flag on '" + DescribeTarget(target)
                            + "' is ignored — set it on the top-most part of each chain only.";
                    }
                }
            }

            if (facingTargetCount == 0)
            {
                return "no target on rig '" + slot.rig.name + "' has Faces Direction set, so the "
                    + "facing resolves and the variant is picked but no part mirrors - nothing will "
                    + "visibly turn, in the preview or at run time.";
            }
            return redundantFlagNote;
        }

        /// <summary>
        /// The nearest target above <paramref name="target"/> in the rig's own hierarchy that also
        /// mirrors, or null. Reads <c>sourceNodePath</c>, which is authoring-only data — a rig that
        /// never recorded paths simply reports nothing rather than guessing at names.
        /// </summary>
        private static RigTargetDefinition FindMirroredAncestor(RigAsset rig, RigTargetDefinition target)
        {
            if (string.IsNullOrEmpty(target.sourceNodePath))
            {
                return null;
            }
            RigTargetDefinition nearest = null;
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition candidate = rig.targets[targetIndex];
                if (candidate == null || candidate == target || !candidate.facesDirection
                    || string.IsNullOrEmpty(candidate.sourceNodePath))
                {
                    continue;
                }
                if (!target.sourceNodePath.StartsWith(candidate.sourceNodePath + "/", System.StringComparison.Ordinal))
                {
                    continue;
                }
                if (nearest == null || candidate.sourceNodePath.Length > nearest.sourceNodePath.Length)
                {
                    nearest = candidate;
                }
            }
            return nearest;
        }

        private static string DescribeTarget(RigTargetDefinition target)
        {
            return !string.IsNullOrEmpty(target.displayName)
                ? target.displayName
                : (!string.IsNullOrEmpty(target.sourceNodePath) ? target.sourceNodePath : "0x" + target.tagId.ToString("X8"));
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
