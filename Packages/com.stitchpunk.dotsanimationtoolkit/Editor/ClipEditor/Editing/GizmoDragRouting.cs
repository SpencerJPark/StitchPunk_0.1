// Copyright (c) 2026 Stitch Punk. All rights reserved.

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
        SocketOffset
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
    /// A selected socket wins outright. Sockets are rig structure and have no keyframes at all, so
    /// there is nothing for the clip modes to contribute.
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
        /// <param name="isRigEditMode">Whether Rig Edit mode is on.</param>
        /// <param name="isAutoKeyEnabled">Whether Auto Key is on.</param>
        /// <param name="hasPendingEdit">Whether the drag produced a value to write.</param>
        public static GizmoDragDestination Resolve(
            bool hasSocketSelected,
            bool isRigEditMode,
            bool isAutoKeyEnabled,
            bool hasPendingEdit)
        {
            if (hasSocketSelected)
            {
                return GizmoDragDestination.SocketOffset;
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
    }
}
