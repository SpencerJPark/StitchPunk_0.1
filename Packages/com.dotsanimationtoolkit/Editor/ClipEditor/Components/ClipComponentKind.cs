// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A kind of animation component an object can carry in the clip editor's inspector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A component is a track that exists, not a new field on the asset.</strong> Every kind
    /// here is the presence of something the clip or rig already stores — a transform track, a
    /// flipbook track, a socket definition — so the stack is a view of the asset rather than a
    /// second copy of it. Adding one creates the thing; removing one deletes it. Nothing is
    /// serialized to say "this object has a Flipbook component", because the sprite track being
    /// there is that statement, and two statements could disagree.
    /// </para>
    /// <para>
    /// <strong>Transform is the exception, and is not an add-on.</strong> Everything in an animator
    /// is somewhere, so every object carries a transform whether or not it has been keyed yet — an
    /// authored part and a skinned bone alike, since posing them is the main way either gets
    /// animated. It is therefore never in the Add Component menu and never removable, and its
    /// track is minted by the first key rather than by an act of adding. The other kinds are the
    /// add-ons.
    /// </para>
    /// <para>
    /// <strong>Easing is deliberately absent.</strong> It belongs to a key rather than to an object:
    /// every key has one whether or not anybody wants it, so there is nothing to add and nothing to
    /// remove. The inspector shows it in the key block instead.
    /// </para>
    /// </remarks>
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
        Socket = 4
    }

    /// <summary>
    /// What a component's data belongs to, and therefore what an edit to it changes.
    /// </summary>
    /// <remarks>
    /// The distinction is shown in the inspector rather than kept as an implementation detail,
    /// because it decides how far an edit reaches. A clip-scoped component is this clip's business;
    /// a rig-scoped one is every clip's, and moving it while looking at one animation moves it in
    /// all of them. It also decides which object the undo is recorded on.
    /// </remarks>
    public enum ClipComponentScope : byte
    {
        /// <summary>Stored on the clip. Edits touch this clip only.</summary>
        Clip = 0,

        /// <summary>Stored on the rig asset. Edits are seen by every clip in the set.</summary>
        Rig = 1
    }

    /// <summary>
    /// One component on one object: its kind, and which of that kind it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Addressed by index into the owning list — the same reasoning as <see cref="KeyAddress"/>.
    /// The tracks and socket definitions are plain serializable objects inside lists that an undo or
    /// a delete replaces wholesale, so a held reference survives as a stale copy of something
    /// nothing shows any more, while an index is either still valid or obviously out of range.
    /// </para>
    /// <para>
    /// <strong>An index of −1 is a real state for the intrinsic kinds only.</strong> A transform
    /// component is on every object from the moment it exists, and its track is minted by the first
    /// key — so between those two moments the component is present with nothing to index. For every
    /// other kind −1 still means "nothing was added", because those kinds <em>are</em> the thing
    /// they index.
    /// </para>
    /// </remarks>
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
