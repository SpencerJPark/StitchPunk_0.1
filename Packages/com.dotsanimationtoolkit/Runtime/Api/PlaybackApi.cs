// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The playback API (architecture section 5.4): how a game plays, queues, stops or retimes a
    /// clip on an actor, and asks what it is currently doing.
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
    /// Every method is Burst-compatible and takes the buffer (and, on the read side, the
    /// enabled-ref or registry) directly, so a job that already has them can issue or answer a
    /// request without a structural change or a main-thread hop. Out-of-range layer indices on the
    /// read side answer "no"/"0" rather than throwing: a layer index is a <c>byte</c> and rigs
    /// define between one and eight layers, so a stale index is a routine consequence of swapping a
    /// rig, not a programming error worth an exception in a Burst job.
    /// </para>
    /// <para>
    /// A <c>NaN</c> <c>blendDuration</c> means "use the clip's authored default"
    /// (<c>defaultBlendIn</c> for Play/Queue, <c>defaultBlendOut</c> for Stop) and is the default
    /// argument. It is deliberately not 0: 0 is a meaningful, reachable value — a hard cut — and a
    /// caller who omits the parameter is expressing no opinion rather than asking for a cut.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class PlaybackApi
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

        /// <summary>
        /// Whether <paramref name="layerIndex"/> is actively playing <paramref name="clip"/>.
        /// </summary>
        /// <remarks>
        /// A layer that has finished a <see cref="LoopMode.Once"/> clip is no longer active
        /// (architecture section 5.4 deactivates it on completion), so this answers false for it —
        /// which is what "is it playing" means and what callers polling for "still swinging?" need.
        /// A clip fading <em>out</em> of a crossfade is likewise not playing: it lives in the
        /// layer's <c>previous*</c> slot, and the layer is playing whatever replaced it.
        /// </remarks>
        /// <param name="layers">The actor's playback layers.</param>
        /// <param name="layerIndex">The layer to inspect.</param>
        /// <param name="clip">The clip id to test for.</param>
        /// <returns>True when the layer is active and its current clip is <paramref name="clip"/>.</returns>
        [BurstCompile]
        public static bool IsPlaying(in DynamicBuffer<PlaybackLayer> layers, byte layerIndex, ClipId clip)
        {
            if (layerIndex >= layers.Length)
            {
                return false;
            }

            PlaybackLayer layer = layers[layerIndex];
            if ((layer.flags & PlaybackFlags.Active) == 0 || layer.clipIndex < 0)
            {
                return false;
            }
            return layer.clip.Value == clip.Value;
        }

        /// <summary>
        /// The layer's progress through its current clip, in [0, 1] — the value a game wants for
        /// "am I past the wind-up yet?" (architecture section 5.4, amendment A26).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Why this takes the registry.</strong> <see cref="PlaybackLayer.time"/> is
        /// seconds on the un-wrapped timeline; turning that into a fraction needs the clip's
        /// duration, which lives in <see cref="ClipBlob"/> inside the registry blob and is reachable
        /// from nothing the layer buffer holds. Amendment A26 records the alternatives that were
        /// rejected — caching a duration on the layer, or returning raw seconds under a name that
        /// promises a fraction. Callers already hold the registry: it is
        /// <see cref="ClipRegistry.Value"/> on the same actor root.
        /// </para>
        /// <para>
        /// The result is the <em>mapped</em> time, so a looping clip reports its position within the
        /// current lap rather than a number that climbs past 1 forever, and a PingPong clip reports
        /// its reflected position.
        /// </para>
        /// </remarks>
        /// <param name="layers">The actor's playback layers.</param>
        /// <param name="registry">The actor's baked clip registry.</param>
        /// <param name="layerIndex">The layer to inspect.</param>
        /// <returns>
        /// Normalized time in [0, 1]; 0 for an out-of-range layer, an inactive layer, an unresolved
        /// clip index, or a clip whose duration is not positive.
        /// </returns>
        [BurstCompile]
        public static float NormalizedTime(
            in DynamicBuffer<PlaybackLayer> layers,
            ref ClipRegistryBlob registry,
            byte layerIndex)
        {
            if (layerIndex >= layers.Length)
            {
                return 0f;
            }

            PlaybackLayer layer = layers[layerIndex];
            if ((layer.flags & PlaybackFlags.Active) == 0)
            {
                return 0f;
            }
            if (layer.clipIndex < 0 || layer.clipIndex >= registry.clips.Length)
            {
                return 0f;
            }

            ref ClipBlob clip = ref registry.clips[layer.clipIndex];

            // Validation rule V01 guarantees at least 1 ms, so this branch is unreachable through
            // the authoring pipeline. It exists so a hand-built or corrupted registry divides by
            // nothing — the same guard ClipSampler.MapTimeNormalized carries, restated here because
            // returning early is cheaper than resolving a loop mode that cannot matter.
            if (clip.duration <= 0f)
            {
                return 0f;
            }

            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);
            return ClipSampler.MapTimeNormalized(layer.time, clip.duration, resolvedLoopMode);
        }

        /// <summary>
        /// Whether a <see cref="LoopMode.Once"/> clip on <paramref name="layerIndex"/> completed
        /// during the most recent playback advance.
        /// </summary>
        /// <remarks>
        /// True for exactly one frame per completion: <c>PlaybackTimeSystem</c> clears the flag at
        /// the top of every advance and sets it again only on the frame the clip ends. A caller that
        /// runs before <c>AnimationToolkitLogicSystemGroup</c> therefore observes the previous
        /// frame's completion — the same one-frame latency contract animation events carry
        /// (architecture section 5.5). Callers needing same-frame completion order themselves after
        /// <see cref="AnimationToolkitSystemGroup"/>.
        /// </remarks>
        /// <param name="layers">The actor's playback layers.</param>
        /// <param name="layerIndex">The layer to inspect.</param>
        /// <returns>True when the layer finished on the most recent advance.</returns>
        [BurstCompile]
        public static bool HasFinishedThisFrame(in DynamicBuffer<PlaybackLayer> layers, byte layerIndex)
        {
            if (layerIndex >= layers.Length)
            {
                return false;
            }
            return (layers[layerIndex].flags & PlaybackFlags.FinishedThisFrame) != 0;
        }
    }
}
