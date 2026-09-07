// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Per-layer playback state on the actor root. One element per rig layer; the buffer index is
    /// the layer index, and higher indices composite later. Written only by the package's
    /// command/time systems; games read it through <c>PlaybackApi</c>.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct PlaybackLayer : IBufferElementData
    {
        public ClipId clip;

        public int clipIndex; // -1 = none/unresolved

        public float time; // seconds on the clip's un-wrapped timeline

        public float timeAtFrameStart; // written only by PlaybackTimeSystem, before it advances time

        public float speed; // may be negative (reverse playback)

        public LoopMode loop; // UseClipDefault resolves to the clip's default

        public ClipId previousClip; // crossfade-source clip (the blend "previous" slot)

        public int previousClipIndex; // -1 = none

        public float previousTime;

        public float previousSpeed;

        public LoopMode previousLoop; // captured when the clip moved into the previous slot, so the outgoing clip keeps its own loop through the crossfade rather than inheriting the new clip's

        public float blendElapsed;

        public float blendDuration; // 0 = not blending

        public ClipId queuedClip; // one-deep queue, promoted when the current clip finishes

        public float queuedSpeed;

        public LoopMode queuedLoop;

        public float queuedBlend;

        public PlaybackFlags flags;

        public uint animationKey; // 0 = driven by a raw clip Play
    }
}
