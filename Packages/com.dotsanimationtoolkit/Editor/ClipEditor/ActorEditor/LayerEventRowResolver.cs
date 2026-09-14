// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    public struct LayerEventRowState
    {
        public bool emits;
        public bool ghostPrevious;
        public float time; // seconds, mapped onto [0, duration]
        public float duration; // seconds; 0 when the layer has no clip
        public float normalizedTime;
        public LoopMode resolvedLoop;
    }

    /// <summary>
    /// How one Actor Editor layer row draws: whether the layer emits its markers the way the runtime
    /// does, whether its crossfade source is ghosted, and where its loop-resolved playhead sits.
    /// </summary>
    public static class LayerEventRowResolver
    {
        public static LayerEventRowState Resolve(in PlaybackLayer layer, ClipAsset currentClip, ClipAsset previousClip)
        {
            LayerEventRowState state = new LayerEventRowState { resolvedLoop = LoopMode.Once };

            // Keyed on the Blending flag, as the runtime is, not on blendDuration.
            state.ghostPrevious = (layer.flags & PlaybackFlags.Blending) != 0
                && layer.previousClipIndex >= 0
                && previousClip != null;

            if (currentClip == null || layer.clipIndex < 0)
            {
                return state;
            }

            // A Once clip that finished this frame is already inactive but still emits its last crossings.
            state.emits = (layer.flags & (PlaybackFlags.Active | PlaybackFlags.FinishedThisFrame)) != 0;
            state.resolvedLoop = ClipSampler.ResolveLoopMode(layer.loop, currentClip.defaultLoop);
            state.duration = currentClip.duration;
            state.normalizedTime = ClipSampler.MapTimeNormalized(layer.time, currentClip.duration, state.resolvedLoop);
            state.time = state.normalizedTime * state.duration;
            return state;
        }

        public static ClipAsset FindClipAsset(ActorProfileAsset profile, ClipId clipId)
        {
            if (profile == null || profile.clipSets == null || !clipId.IsValid)
            {
                return null;
            }

            for (int clipSetIndex = 0; clipSetIndex < profile.clipSets.Count; clipSetIndex++)
            {
                ClipSetAsset clipSet = profile.clipSets[clipSetIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }

                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip != null && clip.Id == clipId)
                    {
                        return clip;
                    }
                }
            }

            return null;
        }
    }
}
