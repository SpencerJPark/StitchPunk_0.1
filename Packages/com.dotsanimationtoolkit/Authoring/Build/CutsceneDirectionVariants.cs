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
        /// Two ways facing goes silently inert, both of which cost an owner checkpoint on
        /// 2026-09-06. <strong>Nothing opted in:</strong> <c>facesDirection</c> is what bakes
        /// <c>PartFacing</c> and what gates the editor's own mirror, so a rig where no target sets
        /// it resolves a facing, picks a variant, and turns nothing. <strong>A part opted in under
        /// another:</strong> the mirror negates a part's local x scale, so a mirrored part inside a
        /// mirrored parent multiplies to +1 and cancels — on a nested rig that flips every second
        /// level, which reads as a character whose head does not turn with its body. Opt in the
        /// top-most part of each chain and its subtree inherits the reflection exactly once.
        /// </remarks>
        internal static string DescribeFacingRigProblem(CutsceneSlot slot)
        {
            if (slot == null || slot.kind != CutsceneSlotKind.Actor
                || slot.directionSet == null || slot.rig == null || slot.rig.targets == null)
            {
                return null;
            }

            int facingTargetCount = 0;
            for (int targetIndex = 0; targetIndex < slot.rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = slot.rig.targets[targetIndex];
                if (target == null || !target.facesDirection)
                {
                    continue;
                }
                facingTargetCount++;

                RigTargetDefinition mirroredAncestor = FindMirroredAncestor(slot.rig, target);
                if (mirroredAncestor != null)
                {
                    return "'" + DescribeTarget(target) + "' has Faces Direction set inside '"
                        + DescribeTarget(mirroredAncestor) + "', which also has it. A mirrored part "
                        + "inside a mirrored parent cancels back to unmirrored, so only every second "
                        + "level of the rig flips. Set it on the top-most part of each chain only.";
                }
            }

            if (facingTargetCount == 0)
            {
                return "no target on rig '" + slot.rig.name + "' has Faces Direction set, so the "
                    + "facing resolves and the variant is picked but no part mirrors - nothing will "
                    + "visibly turn, in the preview or at run time.";
            }
            return null;
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
