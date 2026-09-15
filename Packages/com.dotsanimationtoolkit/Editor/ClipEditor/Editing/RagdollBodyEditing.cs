// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Undo-recorded edits to a rig's ragdoll bodies and rig-wide ragdoll settings.</summary>
    public static class RagdollBodyEditing
    {
        public static RagdollBodyDefinition AddBody(RigAsset rig, in RigNodeAddress address, string displayName)
        {
            return null;
        }

        public static bool RemoveBody(RigAsset rig, uint bodyId)
        {
            return false;
        }

        public static void SetHingeLimit(RigAsset rig, uint bodyId, float minimumDegrees, float maximumDegrees)
        {
        }

        public static void SetConeLimit(RigAsset rig, uint bodyId, float swingDegrees, float twistDegrees)
        {
        }

        public static void ApplyBodyEdit(RigAsset rig, string undoLabel)
        {
        }

        public static void ApplyRigSettingsEdit(RigAsset rig, in RagdollRigSettings settings, string undoLabel)
        {
        }

        public static RagdollBodyDefinition FindBodyById(RigAsset rig, uint bodyId)
        {
            return null;
        }
    }
}
