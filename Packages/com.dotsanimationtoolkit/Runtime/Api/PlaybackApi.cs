// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Every method appends to the command buffer and enables <see cref="AnimationCommandPending"/>.
    /// <c>blendDuration = NaN</c> means use the clip's authored default.
    /// </summary>
    [BurstCompile]
    public static class PlaybackApi
    {
        /// <param name="blendDuration">NaN uses the clip's authored default; 0 is a hard cut.</param>
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
        /// clip finishes; a second Queue replaces the first.
        /// </summary>
        /// <param name="blendDuration">NaN uses the clip's authored default; 0 is a hard cut.</param>
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

        /// <param name="blendDuration">NaN uses the clip's authored default blend-out; 0 deactivates immediately.</param>
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
        /// Sets playback speed on <paramref name="layerIndex"/> without touching its clip, time, or
        /// blend. Negative values play in reverse.
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
        /// un-wrapped timeline.
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
        /// Requests a named entry from the actor's <see cref="ActorProfile"/>, resolved against
        /// <see cref="ActorFacing"/> at apply time. <see cref="CommandApplySystem"/> routes it to
        /// whichever layer the entry actually lives on.
        /// </summary>
        /// <param name="speed">NaN uses the entry's authored speed.</param>
        /// <param name="loop">UseClipDefault uses the entry's authored loop mode.</param>
        /// <param name="blendDuration">NaN uses the entry's authored blend-in.</param>
        public static void PlayAnimation(
            ref DynamicBuffer<AnimationCommand> commands,
            EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
            uint animationKey,
            float speed = float.NaN,
            LoopMode loop = LoopMode.UseClipDefault,
            float blendDuration = float.NaN)
        {
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.PlayAnimation,
                layerIndex = 0,
                clip = default,
                speed = speed,
                loop = loop,
                blendDuration = blendDuration,
                time = 0f,
                animationKey = animationKey
            });
            commandPendingEnabled.ValueRW = true;
        }

        /// <summary>
        /// Stops whichever layer is currently playing <paramref name="animationKey"/>; a no-op if
        /// that key is not the active one on its entry's layer.
        /// </summary>
        /// <param name="blendDuration">NaN uses the entry's authored blend-out.</param>
        public static void StopAnimation(
            ref DynamicBuffer<AnimationCommand> commands,
            EnabledRefRW<AnimationCommandPending> commandPendingEnabled,
            uint animationKey,
            float blendDuration = float.NaN)
        {
            commands.Add(new AnimationCommand
            {
                kind = CommandKind.StopAnimation,
                layerIndex = 0,
                clip = default,
                speed = 0f,
                loop = LoopMode.UseClipDefault,
                blendDuration = blendDuration,
                time = 0f,
                animationKey = animationKey
            });
            commandPendingEnabled.ValueRW = true;
        }

        /// <summary>True when any layer's active clip was started by <see cref="PlayAnimation"/> with this key.</summary>
        [BurstCompile]
        public static bool IsAnimationPlaying(in DynamicBuffer<PlaybackLayer> layers, uint animationKey)
        {
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                PlaybackLayer layer = layers[layerIndex];
                if (layer.animationKey == animationKey && (layer.flags & PlaybackFlags.Active) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when <paramref name="layerIndex"/> is active and playing <paramref name="clip"/>; a
        /// completed <see cref="LoopMode.Once"/> clip or one fading out of a crossfade is not "playing".
        /// </summary>
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
        /// Progress through <paramref name="layerIndex"/>'s current clip in [0, 1], mapped for loop
        /// mode: a looping clip reports lap position, PingPong reports reflected position.
        /// </summary>
        /// <returns>0 for an out-of-range or inactive layer, an unresolved clip index, or non-positive clip duration.</returns>
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

            // Authoring validation guarantees a positive duration; this guards a hand-built or
            // corrupted registry against dividing by zero.
            if (clip.duration <= 0f)
            {
                return 0f;
            }

            LoopMode resolvedLoopMode = ClipSampler.ResolveLoopMode(layer.loop, clip.defaultLoop);
            return ClipSampler.MapTimeNormalized(layer.time, clip.duration, resolvedLoopMode);
        }

        /// <summary>
        /// True for exactly one frame after a <see cref="LoopMode.Once"/> clip completes on
        /// <paramref name="layerIndex"/>; a caller running before <see cref="AnimationToolkitSystemGroup"/>
        /// sees the previous frame's completion.
        /// </summary>
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
