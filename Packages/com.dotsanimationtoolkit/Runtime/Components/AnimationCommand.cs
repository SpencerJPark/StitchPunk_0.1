// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// One playback request on the actor's command buffer. Games never write elements by hand —
    /// they call <c>PlaybackApi</c>, which appends the element and enables
    /// <see cref="AnimationCommandPending"/>. <c>CommandApplySystem</c> drains the buffer each frame.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct AnimationCommand : IBufferElementData
    {
        public CommandKind kind;

        public byte layerIndex;

        public ClipId clip; // Play/Queue

        public float speed; // Play/Queue/SetSpeed

        public LoopMode loop; // Play/Queue; UseClipDefault = clip default

        public float blendDuration; // Play/Queue: crossfade-in; Stop: fade-out; NaN = clip's default

        public float time; // SetTime target, seconds
    }

    /// <summary>
    /// Enableable gate on the actor root: enabled while unapplied commands sit in the
    /// <see cref="AnimationCommand"/> buffer, so command-less actors cost nothing to the apply
    /// system's query. Baked disabled.
    /// </summary>
    public struct AnimationCommandPending : IComponentData, IEnableableComponent
    {
    }
}
