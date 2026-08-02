// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// The write side of the playback API (architecture section 5.4): how a game asks an actor to
    /// play, queue, stop or retime a clip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Games never write <see cref="AnimationCommand"/> elements by hand.</strong> Every
    /// request has two halves — append the element, and enable
    /// <see cref="AnimationCommandPending"/> — and doing only the first produces an actor that
    /// silently ignores the request until some unrelated command wakes it up. That is a bug with no
    /// error message and a plausible innocent explanation ("the animation just didn't play"), so the
    /// pairing is not left to callers.
    /// </para>
    /// <para>
    /// Every method is Burst-compatible and takes the buffer and the enabled-ref directly, so a job
    /// that already has them can issue commands without a structural change or a main-thread hop.
    /// </para>
    /// <para>
    /// A <c>NaN</c> <c>blendDuration</c> means "use the clip's authored default"
    /// (<c>defaultBlendIn</c> for Play/Queue, <c>defaultBlendOut</c> for Stop) and is the default
    /// argument. It is deliberately not 0: 0 is a meaningful, reachable value — a hard cut — and a
    /// caller who omits the parameter is expressing no opinion rather than asking for a cut.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class AnimationCommandUtil
    {
        /// <summary>
        /// Plays <paramref name="clip"/> on <paramref name="layerIndex"/>, crossfading from whatever
        /// that layer was playing.
        /// </summary>
        /// <param name="blendDuration">
        /// Crossfade seconds. <c>NaN</c> (the default) uses the clip's authored <c>defaultBlendIn</c>;
        /// 0 is a hard cut.
        /// </param>
        public static void Play(
            ref DynamicBuffer<AnimationCommand> commands,
            EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
            byte layerIndex,
            ClipId clip,
            float speed = 1f,
            LoopMode loop = LoopMode.UseClipDefault,
            float blendDuration = float.NaN)
        {
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.Play,
                layerIndex = layerIndex,
                clip = clip,
                speed = speed,
                loop = loop,
                blendDuration = blendDuration,
                time = 0f
            });
            commandPendingEnabled.ValueRW = true;
        }

        /// <summary>
        /// Queues <paramref name="clip"/> to start on <paramref name="layerIndex"/> when the current
        /// clip finishes. The queue is one deep by design (architecture section 5.4); a second Queue
        /// replaces the first, and deeper sequencing is game-side state.
        /// </summary>
        public static void Queue(
            ref DynamicBuffer<AnimationCommand> commands,
            EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
            byte layerIndex,
            ClipId clip,
            float speed = 1f,
            LoopMode loop = LoopMode.UseClipDefault,
            float blendDuration = float.NaN)
        {
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.Queue,
                layerIndex = layerIndex,
                clip = clip,
                speed = speed,
                loop = loop,
                blendDuration = blendDuration,
                time = 0f
            });
            commandPendingEnabled.ValueRW = true;
        }

        /// <summary>
        /// Stops <paramref name="layerIndex"/>, fading the current clip out over
        /// <paramref name="blendDuration"/> seconds. <c>NaN</c> uses the clip's authored
        /// <c>defaultBlendOut</c>; 0 deactivates immediately.
        /// </summary>
        public static void Stop(
            ref DynamicBuffer<AnimationCommand> commands,
            EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
            byte layerIndex,
            float blendDuration = float.NaN)
        {
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.Stop,
                layerIndex = layerIndex,
                clip = default,
                speed = 0f,
                loop = LoopMode.UseClipDefault,
                blendDuration = blendDuration,
                time = 0f
            });
            commandPendingEnabled.ValueRW = true;
        }

        /// <summary>
        /// Sets the playback speed of <paramref name="layerIndex"/> without disturbing its time,
        /// clip or blend. Negative values play in reverse.
        /// </summary>
        public static void SetSpeed(
            ref DynamicBuffer<AnimationCommand> commands,
            EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
            byte layerIndex,
            float speed)
        {
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.SetSpeed,
                layerIndex = layerIndex,
                clip = default,
                speed = speed,
                loop = LoopMode.UseClipDefault,
                blendDuration = float.NaN,
                time = 0f
            });
            commandPendingEnabled.ValueRW = true;
        }

        /// <summary>
        /// Scrubs <paramref name="layerIndex"/> to <paramref name="time"/> seconds on the clip's
        /// un-wrapped timeline, leaving speed, clip and blend alone.
        /// </summary>
        public static void SetTime(
            ref DynamicBuffer<AnimationCommand> commands,
            EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
            byte layerIndex,
            float time)
        {
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.SetTime,
                layerIndex = layerIndex,
                clip = default,
                speed = 0f,
                loop = LoopMode.UseClipDefault,
                blendDuration = float.NaN,
                time = time
            });
            commandPendingEnabled.ValueRW = true;
        }
    }
}
