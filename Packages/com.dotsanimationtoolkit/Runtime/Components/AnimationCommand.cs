// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// One playback request on the actor's command buffer (architecture sections 5.2, 5.4). Games
    /// never write elements by hand — they call <c>AnimationCommandUtil</c>, which appends the
    /// element and enables <see cref="AnimationCommandPending"/>. <c>CommandApplySystem</c> drains
    /// the buffer each frame.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct AnimationCommand : IBufferElementData
    {
        /// <summary>Which request this element carries.</summary>
        public CommandKind kind;

        /// <summary>The playback layer the request addresses.</summary>
        public byte layerIndex;

        /// <summary>Clip id for Play/Queue requests.</summary>
        public ClipId clip;

        /// <summary>Playback speed for Play/Queue/SetSpeed requests.</summary>
        public float speed;

        /// <summary>Loop mode for Play/Queue requests; <see cref="LoopMode.UseClipDefault"/> = clip default.</summary>
        public LoopMode loop;

        /// <summary>
        /// Play/Queue: crossfade-in seconds; Stop: fade-out seconds; NaN = use the clip's default
        /// blend (architecture section 5.4).
        /// </summary>
        public float blendDuration;

        /// <summary>Target playback time in seconds for SetTime requests.</summary>
        public float time;
    }

    /// <summary>
    /// Enableable gate on the actor root: enabled while unapplied commands sit in the
    /// <see cref="AnimationCommand"/> buffer, so command-less actors cost nothing to the apply
    /// system's query (architecture section 5.2). Baked disabled.
    /// </summary>
    public struct AnimationCommandPending : IComponentData, IEnableableComponent
    {
    }
}
