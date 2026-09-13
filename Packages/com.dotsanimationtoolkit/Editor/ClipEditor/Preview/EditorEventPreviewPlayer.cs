// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Plays AudioClips through the Editor's internal preview voice for scrubbing event markers.
    /// Contract: one preview voice at a time; the same clip is rate-limited to once per 40 ms.
    /// </summary>
    public sealed class EditorEventPreviewPlayer : IDisposable
    {
        private const double RepeatSuppressionSeconds = 0.04;

        // UnityEditor.AudioUtil is an internal type with no public API surface, so the preview
        // voice can only be reached through reflection.
        private static readonly MethodInfo PlayPreviewClipMethod;
        private static readonly MethodInfo StopAllPreviewClipsMethod;
        private static bool hasLoggedUnavailableWarning;

        private readonly Dictionary<AudioClip, double> lastPlayTimeByClip = new Dictionary<AudioClip, double>();
        private bool hasPlayedAnyClip;

        static EditorEventPreviewPlayer()
        {
            Type audioUtilType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType == null)
            {
                return;
            }

            PlayPreviewClipMethod = audioUtilType.GetMethod(
                "PlayPreviewClip",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(AudioClip), typeof(int), typeof(bool) },
                null);
            StopAllPreviewClipsMethod = audioUtilType.GetMethod("StopAllPreviewClips", BindingFlags.Public | BindingFlags.Static);
        }

        public void Play(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (PlayPreviewClipMethod == null)
            {
                if (!hasLoggedUnavailableWarning)
                {
                    hasLoggedUnavailableWarning = true;
                    Debug.LogWarning("EditorEventPreviewPlayer: editor audio preview is unavailable on this Unity version.");
                }
                return;
            }

            double currentTime = UnityEditor.EditorApplication.timeSinceStartup;
            if (lastPlayTimeByClip.TryGetValue(clip, out double lastPlayTime)
                && currentTime - lastPlayTime < RepeatSuppressionSeconds)
            {
                return;
            }

            lastPlayTimeByClip[clip] = currentTime;
            hasPlayedAnyClip = true;
            PlayPreviewClipMethod.Invoke(null, new object[] { clip, 0, false });
        }

        public void Dispose()
        {
            if (!hasPlayedAnyClip || StopAllPreviewClipsMethod == null)
            {
                return;
            }

            hasPlayedAnyClip = false;
            StopAllPreviewClipsMethod.Invoke(null, null);
        }
    }
}
