// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    public struct RagdollBodySummary
    {
        public int bodyCount;
        public int jointCount;
        public int unresolvedCount;
        public string text;
    }

    /// <summary>Counts a rig's ragdoll bodies and implied joints, and flags bodies whose node no longer resolves.</summary>
    public static class RagdollBodySummaryResolver
    {
        public static RagdollBodySummary Resolve(RigAsset rig)
        {
            RagdollBodySummary bodySummary = new RagdollBodySummary
            {
                bodyCount = 0,
                jointCount = 0,
                unresolvedCount = 0,
                text = "0 bodies · 0 joints",
            };

            if (rig == null || rig.ragdollBodies == null)
            {
                return bodySummary;
            }

            foreach (RagdollBodyDefinition candidateBodyDefinition in rig.ragdollBodies)
            {
                if (candidateBodyDefinition == null)
                {
                    continue;
                }

                bodySummary.bodyCount++;

                if (HasParentBody(rig, candidateBodyDefinition))
                {
                    bodySummary.jointCount++;
                }

                if (!IsBodyResolved(rig, candidateBodyDefinition))
                {
                    bodySummary.unresolvedCount++;
                }
            }

            bodySummary.text = BuildCountedNoun(bodySummary.bodyCount, "body", "bodies") + " · " + BuildCountedNoun(bodySummary.jointCount, "joint", "joints");

            return bodySummary;
        }

        public static bool IsBodyResolved(RigAsset rig, RagdollBodyDefinition body)
        {
            if (body == null)
            {
                return false;
            }

            string resolvedNodePath = ResolveNodePath(rig, body);
            return !string.IsNullOrEmpty(resolvedNodePath);
        }

        public static bool HasParentBody(RigAsset rig, RagdollBodyDefinition body)
        {
            if (body == null || rig == null || rig.ragdollBodies == null)
            {
                return false;
            }

            string thisBodyNodePath = ResolveNodePath(rig, body);
            if (string.IsNullOrEmpty(thisBodyNodePath))
            {
                return false;
            }

            foreach (RagdollBodyDefinition candidateParentBodyDefinition in rig.ragdollBodies)
            {
                if (candidateParentBodyDefinition == null || candidateParentBodyDefinition == body)
                {
                    continue;
                }

                string candidateParentNodePath = ResolveNodePath(rig, candidateParentBodyDefinition);
                if (string.IsNullOrEmpty(candidateParentNodePath))
                {
                    continue;
                }

                bool isStrictAncestorPrefix = thisBodyNodePath.Length > candidateParentNodePath.Length
                    && thisBodyNodePath.StartsWith(candidateParentNodePath, System.StringComparison.Ordinal)
                    && thisBodyNodePath[candidateParentNodePath.Length] == '/';

                if (isStrictAncestorPrefix)
                {
                    return true;
                }
            }

            return false;
        }

        public static string ResolveNodePath(RigAsset rig, RagdollBodyDefinition body)
        {
            if (body == null)
            {
                return string.Empty;
            }

            switch (body.address.kind)
            {
                case RigNodeAddressKind.RigTarget:
                    return ResolveRigTargetSourceNodePath(rig, body.address.targetId);
                case RigNodeAddressKind.HierarchyPath:
                    return body.address.hierarchyPath ?? string.Empty;
                case RigNodeAddressKind.Bone:
                    return body.address.boneName ?? string.Empty;
                default:
                    return string.Empty;
            }
        }

        private static string ResolveRigTargetSourceNodePath(RigAsset rig, uint targetId)
        {
            if (rig == null || rig.targets == null)
            {
                return string.Empty;
            }

            foreach (RigTargetDefinition candidateTargetDefinition in rig.targets)
            {
                if (candidateTargetDefinition != null && candidateTargetDefinition.Id.Value == targetId)
                {
                    return candidateTargetDefinition.sourceNodePath ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string BuildCountedNoun(int count, string singularNoun, string pluralNoun)
        {
            string noun = count == 1 ? singularNoun : pluralNoun;
            return count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + noun;
        }
    }
}
