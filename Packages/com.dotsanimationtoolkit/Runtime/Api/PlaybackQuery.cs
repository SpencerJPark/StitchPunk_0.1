// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The read side of the playback API (architecture section 5.4): how a game asks what an actor
    /// is currently playing, without ever indexing the <see cref="PlaybackLayer"/> buffer by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buffer is package-owned state whose fields mean different things in different states —
    /// <c>time</c> is un-wrapped seconds, <c>previousTime</c> belongs to a crossfade source, and
    /// <c>clipIndex</c> is a dense registry index that is <c>-1</c> more often than callers expect.
    /// Every method here answers a question a game actually has, so that the buffer's internal
    /// invariants stay internal and can change without breaking consumers.
    /// </para>
    /// <para>
    /// Every method is Burst-compatible and takes the buffer directly, so a job that already has it
    /// can ask without a main-thread hop. Out-of-range layer indices answer "no" rather than
    /// throwing: a layer index is a <c>byte</c> and rigs define between one and eight layers, so a
    /// stale index is a routine consequence of swapping a rig, not a programming error worth an
    /// exception in a Burst job.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class PlaybackQuery
    {
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
        public static bool FinishedThisFrame(in DynamicBuffer<PlaybackLayer> layers, byte layerIndex)
        {
            if (layerIndex >= layers.Length)
            {
                return false;
            }
            return (layers[layerIndex].flags & PlaybackFlags.FinishedThisFrame) != 0;
        }
    }
}
