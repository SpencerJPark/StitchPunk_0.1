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
    /// serialized to say "this object has a Transform component", because the transform track being
    /// there is that statement, and two statements could disagree.
    /// </para>
    /// <para>
    /// <strong>Easing is deliberately absent.</strong> It belongs to a key rather than to an object:
    /// every key has one whether or not anybody wants it, so there is nothing to add and nothing to
    /// remove. The inspector shows it in the key block instead.
    /// </para>
    /// </remarks>
    public enum ClipComponentKind : byte
    {
        /// <summary>A cutout part's keyed TRS — <c>TransformTrack</c>.</summary>
        Transform = 0,

        /// <summary>A skeleton bone's keyed local TRS — <c>BoneTrack</c>.</summary>
        BoneTransform = 1,

        /// <summary>A keyed sprite-frame index — <c>SpriteTrack</c>. A part may carry several.</summary>
        Flipbook = 2,

        /// <summary>Keyed billboard facing for a billboard root — <c>BillboardTrack</c>.</summary>
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
    /// Addressed by index into the owning list — the same reasoning as <see cref="KeyAddress"/>.
    /// The tracks and socket definitions are plain serializable objects inside lists that an undo or
    /// a delete replaces wholesale, so a held reference survives as a stale copy of something
    /// nothing shows any more, while an index is either still valid or obviously out of range.
    /// </remarks>
    public readonly struct ClipComponentInstance
    {
        public readonly ClipComponentKind kind;

        /// <summary>Index into the clip's track list, or into the rig's socket list.</summary>
        public readonly int index;

        public ClipComponentInstance(ClipComponentKind kind, int index)
        {
            this.kind = kind;
            this.index = index;
        }
    }
}
