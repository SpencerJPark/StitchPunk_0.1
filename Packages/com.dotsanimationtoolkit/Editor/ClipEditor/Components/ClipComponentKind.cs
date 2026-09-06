// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A kind of animation component an object can carry in the clip editor's inspector. A
    /// component is a track that exists, not a new field on the asset — adding one creates the
    /// track and removing one deletes it, so the stack is a view of the asset, never a second copy.
    /// </summary>
    public enum ClipComponentKind : byte
    {
        /// <summary>A part's keyed TRS — <c>TransformTrack</c>. Intrinsic.</summary>
        Transform = 0,

        /// <summary>A skeleton node's keyed local TRS — <c>BoneTrack</c>. Intrinsic.</summary>
        BoneTransform = 1,

        /// <summary>A keyed sprite-frame index — <c>SpriteTrack</c>. A part may carry several.</summary>
        Flipbook = 2,

        /// <summary>
        /// This node faces the viewer — a <c>BillboardRootDefinition</c> on the rig, animated by the
        /// clip's <c>BillboardTrack</c>s.
        /// </summary>
        Billboard = 3,

        /// <summary>An attachment point hung off this object — <c>SocketDefinition</c> on the rig.</summary>
        Socket = 4,

        /// <summary>
        /// A box collider this node falls with when the rig's ragdoll drops — the presence of a
        /// <c>RagdollBodyDefinition</c> on the rig. Works on an authored guiding part and an
        /// imported skinned-mesh bone alike.
        /// </summary>
        Ragdoll = 5
    }

    /// <summary>
    /// What a component's data belongs to, and therefore what an edit to it changes and which
    /// object the undo is recorded on.
    /// </summary>
    public enum ClipComponentScope : byte
    {
        /// <summary>Stored on the clip. Edits touch this clip only.</summary>
        Clip = 0,

        /// <summary>Stored on the rig asset. Edits are seen by every clip in the set.</summary>
        Rig = 1
    }

    /// <summary>
    /// One component on one object: its kind, and which of that kind it is. Addressed by index into
    /// the owning list, like <see cref="KeyAddress"/>, so a held reference cannot go stale.
    /// </summary>
    public readonly struct ClipComponentInstance
    {
        /// <summary>The index an intrinsic component carries before its track exists.</summary>
        public const int NoTrackIndex = -1;

        public readonly ClipComponentKind kind;

        /// <summary>Index into the clip's track list, or into the rig's socket or root list.</summary>
        public readonly int index;

        public ClipComponentInstance(ClipComponentKind kind, int index)
        {
            this.kind = kind;
            this.index = index;
        }

        /// <summary>Whether the thing this component stands for has been created yet.</summary>
        public bool HasTrack
        {
            get { return index >= 0; }
        }
    }
}
