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

        /// <summary>The selected ragdoll body's box on the rig asset (Phase D6, spec §8.3).</summary>
        RagdollBody
    }

    /// <summary>
    /// Decides where a gizmo drag goes, given the modes and selection in force.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One drag gesture has four possible destinations, and mixing them up is expensive.</strong>
    /// A drag can key a clip, hold an unkeyed value, write a prefab asset, or move a socket on the
    /// rig. None of those is recoverable by doing another, and three of the four write to different
    /// assets. The rule for choosing between them therefore lives here, as a table a person can read
    /// in one sitting, rather than spread across the branches of an event handler where the
    /// interesting case — "can this possibly key while Rig Edit is on?" — has to be reconstructed by
    /// tracing.
    /// </para>
    /// <para>
    /// The ordering is the whole content of the rule:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// A selected socket, or a selected ragdoll body, wins outright — tested in that order, though
    /// the two are never both selected at once. Neither has keyframes at all (a socket's offset and
    /// a ragdoll body's box are rig structure, spec §3.3), so there is nothing for the clip modes to
    /// contribute. Ragdoll follows the exact call socket placement already makes (spec §8.3:
    /// "placing a box is a rig edit but not a hierarchy edit") — live whenever the body is selected,
    /// not only in Rig Edit mode.
    /// </description></item>
    /// <item><description>
    /// Rig Edit wins next, and <strong>never yields a clip key</strong>. This is the safety property
    /// the mode exists for: the viewport says in three places that a drag edits the rig, and it must
    /// be true regardless of what Auto Key happens to be set to.
    /// </description></item>
    /// <item><description>
    /// Otherwise Auto Key decides between writing a key now and holding the value.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static class GizmoDragRouting
    {
        /// <summary>
        /// Resolves a finished drag's destination.
        /// </summary>
        /// <param name="hasSocketSelected">Whether the selection is a socket.</param>
        /// <param name="hasRagdollBodySelected">Whether the selection is a ragdoll body (spec §8.3).</param>
        /// <param name="isRigEditMode">Whether Rig Edit mode is on.</param>
        /// <param name="isAutoKeyEnabled">Whether Auto Key is on.</param>
        /// <param name="hasPendingEdit">Whether the drag produced a value to write.</param>
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

        /// <summary>
        /// Whether a non-socket, non-ragdoll transform gizmo should be shown for the current
        /// selection and mode, and whether a drag on it can begin.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the gate <see cref="Resolve"/>'s <c>RigBasePose</c>/<c>ClipKey</c> split assumes
        /// already happened -- a drag has to be able to <em>start</em> before its destination
        /// matters. Rig Edit and clip authoring ask two different questions to get there:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <strong>Rig Edit</strong> writes the prefab's base pose, which has nothing to do with a
        /// clip and nothing to do with whether the rig declares this node a target -- a bare
        /// grouping transform or a skinned bone is exactly what gets repositioned while restructuring
        /// a rig. So the only question is whether a node is selected at all.
        /// </description></item>
        /// <item><description>
        /// <strong>Clip authoring</strong> writes a <c>TransformTrack</c> keyed by a rig target's
        /// stable id, into a clip asset. Both have to exist.
        /// </description></item>
        /// </list>
        /// </remarks>
        /// <param name="isRigEditMode">Whether Rig Edit mode is on.</param>
        /// <param name="hasActiveHierarchyItem">
        /// Whether a hierarchy node is selected (Rig Edit's only requirement).
        /// </param>
        /// <param name="hasTargetSelected">
        /// Whether the selection is a declared rig target (clip authoring's target-id requirement).
        /// </param>
        /// <param name="hasClipSelected">Whether a clip is open (clip authoring's storage requirement).</param>
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
