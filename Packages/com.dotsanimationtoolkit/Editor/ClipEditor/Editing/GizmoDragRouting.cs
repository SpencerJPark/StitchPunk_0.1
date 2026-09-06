// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Where a finished gizmo drag's value is written.</summary>
    public enum GizmoDragDestination
    {
        /// <summary>Nowhere: there was no held value, or nothing selected to write it to.</summary>
        Nothing,

        /// <summary>A keyframe on the selected clip, at the playhead.</summary>
        ClipKey,

        /// <summary>
        /// Held on the clip as a modified-but-unkeyed value, waiting for an explicit Key.
        /// </summary>
        HeldClipEdit,

        /// <summary>The prefab's base pose, through Unity's prefab APIs.</summary>
        RigBasePose,

        /// <summary>The selected socket's offset on the rig asset.</summary>
        SocketOffset,

        /// <summary>The selected ragdoll body's box on the rig asset.</summary>
        RagdollBody
    }

    /// <summary>
    /// Decides where a gizmo drag goes, given the modes and selection in force: a selected socket
    /// or ragdoll body wins outright, Rig Edit wins next and never yields a clip key, and otherwise
    /// Auto Key decides between writing a key now and holding the value.
    /// </summary>
    public static class GizmoDragRouting
    {
        /// <summary>Resolves a finished drag's destination.</summary>
        public static GizmoDragDestination Resolve(
            bool hasSocketSelected,
            bool hasRagdollBodySelected,
            bool isRigEditMode,
            bool isAutoKeyEnabled,
            bool hasPendingEdit)
        {
            if (hasSocketSelected)
            {
                return GizmoDragDestination.SocketOffset;
            }
            if (hasRagdollBodySelected)
            {
                return GizmoDragDestination.RagdollBody;
            }
            if (!hasPendingEdit)
            {
                return GizmoDragDestination.Nothing;
            }
            if (isRigEditMode)
            {
                return GizmoDragDestination.RigBasePose;
            }
            return isAutoKeyEnabled
                ? GizmoDragDestination.ClipKey
                : GizmoDragDestination.HeldClipEdit;
        }

        // Whether a non-socket, non-ragdoll transform gizmo should be shown for the current
        // selection and mode. Rig Edit needs only a selected node; clip authoring needs a declared
        // rig target and an open clip.
        public static bool ShouldShowTransformGizmo(
            bool isRigEditMode,
            bool hasActiveHierarchyItem,
            bool hasTargetSelected,
            bool hasClipSelected)
        {
            return isRigEditMode
                ? hasActiveHierarchyItem
                : hasTargetSelected && hasClipSelected;
        }
    }
}
