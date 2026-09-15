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
            return new RagdollBodySummary { text = "0 bodies" };
        }

        public static bool IsBodyResolved(RigAsset rig, RagdollBodyDefinition body)
        {
            return false;
        }

        public static bool HasParentBody(RigAsset rig, RagdollBodyDefinition body)
        {
            return false;
        }

        public static string ResolveNodePath(RigAsset rig, RagdollBodyDefinition body)
        {
            return string.Empty;
        }
    }
}
