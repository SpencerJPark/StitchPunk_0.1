// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Reads a slot's direction set into the turn table a clip block bakes. Shared by
    /// <see cref="CutsceneBlobBuilder"/> and the Cutscene Editor's preview so both agree on which
    /// clips a set offers.
    /// </summary>
    internal static class CutsceneDirectionVariants
    {
        /// <summary>
        /// Whether a slot's rig can actually show the facing the cutscene resolves for it, and what
        /// is wrong when it cannot. Null means nothing is wrong.
        /// </summary>
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
                // No target opted in: facesDirection is what bakes PartFacing and gates the mirror,
                // so with none set the facing resolves and the variant is picked but nothing mirrors.
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

    }
}
