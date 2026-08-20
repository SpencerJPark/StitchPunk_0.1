// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Per-layer playback state on the actor root (architecture sections 5.2, 5.4). One element
    /// per rig layer; the buffer index is the layer index, and higher indices composite later
    /// (bottom-up composition, section 5.6). Written only by the package's command/time systems;
    /// games read it through <c>PlaybackQuery</c>.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct PlaybackLayer : IBufferElementData
    {
        /// <summary>Id of the clip currently playing on this layer.</summary>
        public ClipId clip;

        /// <summary>Dense registry index of the current clip; −1 = none/unresolved.</summary>
        public int clipIndex;

        /// <summary>Current playback time in seconds on the clip's un-wrapped timeline.</summary>
        public float time;

        /// <summary>
        /// <see cref="time"/> as it stood at the start of this frame's advance — the opening edge of
        /// the window <c>EventEmissionSystem</c> collects marker crossings over (architecture
        /// section 5.5, amendment A27).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Written by <c>PlaybackTimeSystem</c> and by nothing else: it snapshots <see cref="time"/>
        /// immediately before advancing it, and re-snapshots it when a queue promotion restarts the
        /// timeline mid-advance. It exists because event emission is specified to run <em>after</em>
        /// the advance, on a layer whose only record of where it started is the one it just
        /// overwrote. Recomputing the opening edge as <c>time − dt × speed</c> is wrong in exactly
        /// the frames that matter most: a <see cref="LoopMode.Once"/> clip clamps its time at the
        /// end and a queue promotion resets it, so the subtraction describes a window the layer
        /// never travelled and silently drops the markers inside it — including the last markers of
        /// a finishing clip, which is where hit frames live.
        /// </para>
        /// <para>
        /// Commands need no special handling here. <c>CommandApplySystem</c> runs first and moves
        /// <see cref="time"/> (a Play restarts it, a SetTime scrubs it); the snapshot taken
        /// afterwards is of the post-command value, so a jump is never mistaken for playback
        /// travelling across every marker in between.
        /// </para>
        /// </remarks>
        public float advanceStartTime;

        /// <summary>Playback speed multiplier; may be negative (reverse playback).</summary>
        public float speed;

        /// <summary>Requested loop mode; <see cref="LoopMode.UseClipDefault"/> resolves to the clip's default.</summary>
        public LoopMode loop;

        /// <summary>Id of the crossfade-source clip (the blend "previous" slot).</summary>
        public ClipId previousClip;

        /// <summary>Dense registry index of the crossfade-source clip; −1 = none.</summary>
        public int previousClipIndex;

        /// <summary>Playback time of the crossfade-source clip in seconds.</summary>
        public float previousTime;

        /// <summary>Playback speed of the crossfade-source clip.</summary>
        public float previousSpeed;

        /// <summary>
        /// Loop mode the crossfade-source clip was playing under, captured when it moved into the
        /// previous slot. Recorded separately because a command may have overridden
        /// <see cref="loop"/> away from that clip's default, and applying the new clip's request
        /// (or the outgoing clip's unused default) to the outgoing clip would wrap a clip that was
        /// meant to hold — a visible pop in the very transition the crossfade exists to smooth.
        /// <see cref="LoopMode.UseClipDefault"/> resolves to the crossfade-source clip's default.
        /// </summary>
        public LoopMode previousLoop;

        /// <summary>Seconds elapsed in the running crossfade.</summary>
        public float blendElapsed;

        /// <summary>Total crossfade seconds; 0 = not blending.</summary>
        public float blendDuration;

        /// <summary>Id of the one-deep queued clip promoted when the current clip finishes.</summary>
        public ClipId queuedClip;

        /// <summary>Playback speed the queued clip starts with.</summary>
        public float queuedSpeed;

        /// <summary>Loop mode the queued clip starts with.</summary>
        public LoopMode queuedLoop;

        /// <summary>Crossfade seconds used when the queued clip is promoted.</summary>
        public float queuedBlend;

        /// <summary>Playback state flags.</summary>
        public PlaybackFlags flags;
    }
}
