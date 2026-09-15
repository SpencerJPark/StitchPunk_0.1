// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public enum CaptureBackgroundMode
    {
        Transparent,
        SolidColour
    }

    public enum CaptureOutputFormat
    {
        PngSequence,
        Gif
    }

    public struct CaptureSizePreset
    {
        public string label;
        public int width;
        public int height;
    }

    /// <summary>Everything one capture needs besides its source, remembered in EditorPrefs as JSON.</summary>
    [Serializable]
    public sealed class CaptureSettings
    {
        public const string DefaultOutputRoot = "Assets/Generated/DotsAnimationToolkit/Captures";
        public const int MinimumPixelSize = 16;
        public const int MaximumPixelSize = 4096;
        public const int MinimumFramesPerSecond = 1;
        public const int MaximumFramesPerSecond = 60;

        private const string SettingsPrefsKey = "DotsAnimationToolkit.Capture.Settings";
        private const string CameraPosePrefsKeyPrefix = "DotsAnimationToolkit.Capture.CameraPose.";

        public static readonly CaptureSizePreset[] SizePresets = new CaptureSizePreset[]
        {
            new CaptureSizePreset { label = "256 x 256", width = 256, height = 256 },
            new CaptureSizePreset { label = "512 x 512", width = 512, height = 512 },
            new CaptureSizePreset { label = "1024 x 1024", width = 1024, height = 1024 },
            new CaptureSizePreset { label = "1920 x 1080", width = 1920, height = 1080 }
        };

        public int width = 512;
        public int height = 512;
        public int framesPerSecond = 30;

        // Fractions of the source's duration, so switching source keeps a sensible range.
        public float rangeStartNormalized = 0f;
        public float rangeEndNormalized = 1f;

        public CaptureBackgroundMode background = CaptureBackgroundMode.Transparent;
        public Color backgroundColour = new Color(0.17f, 0.17f, 0.18f, 1f);
        public CaptureOutputFormat format = CaptureOutputFormat.PngSequence;

        // Project-relative. Empty means DefaultOutputFolderFor(captureName).
        public string outputFolder = string.Empty;
        public string captureName = string.Empty;

        public static string DefaultOutputFolderFor(string captureName)
        {
            string safeName = string.IsNullOrEmpty(captureName) ? "Capture" : captureName;
            return DefaultOutputRoot + "/" + safeName;
        }

        public string ResolvedOutputFolder
        {
            get
            {
                string trimmedFolder = (outputFolder ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
                return trimmedFolder.Length > 0 ? trimmedFolder : DefaultOutputFolderFor(captureName);
            }
        }

        public void ClampToValidRanges()
        {
            width = Mathf.Clamp(width, MinimumPixelSize, MaximumPixelSize);
            height = Mathf.Clamp(height, MinimumPixelSize, MaximumPixelSize);
            framesPerSecond = Mathf.Clamp(framesPerSecond, MinimumFramesPerSecond, MaximumFramesPerSecond);
            rangeStartNormalized = Mathf.Clamp01(rangeStartNormalized);
            rangeEndNormalized = Mathf.Clamp01(rangeEndNormalized);
            if (rangeEndNormalized < rangeStartNormalized)
            {
                float swappedStart = rangeEndNormalized;
                rangeEndNormalized = rangeStartNormalized;
                rangeStartNormalized = swappedStart;
            }
        }

        public float RangeStartSeconds(float sourceDurationSeconds)
        {
            return Mathf.Clamp01(rangeStartNormalized) * Mathf.Max(0f, sourceDurationSeconds);
        }

        public float RangeEndSeconds(float sourceDurationSeconds)
        {
            return Mathf.Clamp01(rangeEndNormalized) * Mathf.Max(0f, sourceDurationSeconds);
        }

        // The range end is exclusive, so a looping clip's GIF does not repeat its first pose; never fewer than one frame.
        public int FrameCountFor(float sourceDurationSeconds)
        {
            float spanSeconds = RangeEndSeconds(sourceDurationSeconds) - RangeStartSeconds(sourceDurationSeconds);
            int clampedFramesPerSecond = Mathf.Clamp(framesPerSecond, MinimumFramesPerSecond, MaximumFramesPerSecond);
            return Mathf.Max(1, Mathf.CeilToInt(spanSeconds * clampedFramesPerSecond - 0.0001f));
        }

        public float SecondsAtFrame(int zeroBasedFrameIndex, float sourceDurationSeconds)
        {
            int clampedFramesPerSecond = Mathf.Clamp(framesPerSecond, MinimumFramesPerSecond, MaximumFramesPerSecond);
            return RangeStartSeconds(sourceDurationSeconds) + zeroBasedFrameIndex / (float)clampedFramesPerSecond;
        }

        // Browsers treat delays under two centiseconds as ten, so the fastest honest GIF is 50 fps.
        public int GifFrameDelayCentiseconds
        {
            get
            {
                int clampedFramesPerSecond = Mathf.Clamp(framesPerSecond, MinimumFramesPerSecond, MaximumFramesPerSecond);
                return Mathf.Max(2, Mathf.RoundToInt(100f / clampedFramesPerSecond));
            }
        }

        public CaptureSettings Clone()
        {
            return JsonUtility.FromJson<CaptureSettings>(JsonUtility.ToJson(this));
        }

        public static CaptureSettings LoadFromEditorPrefs()
        {
            CaptureSettings loadedSettings = new CaptureSettings();
            string storedJson = EditorPrefs.GetString(SettingsPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(storedJson))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(storedJson, loadedSettings);
                }
                catch (ArgumentException)
                {
                    loadedSettings = new CaptureSettings();
                }
            }
            loadedSettings.ClampToValidRanges();
            return loadedSettings;
        }

        public void SaveToEditorPrefs()
        {
            EditorPrefs.SetString(SettingsPrefsKey, JsonUtility.ToJson(this));
        }

        [Serializable]
        private sealed class StoredCameraPose
        {
            public float yawDegrees;
            public float pitchDegrees;
            public float distance;
            public Vector3 focus;
        }

        public static bool TryLoadCameraPose(string cameraPoseKey, out PreviewCameraPose pose)
        {
            pose = default(PreviewCameraPose);
            if (string.IsNullOrEmpty(cameraPoseKey))
            {
                return false;
            }
            string storedJson = EditorPrefs.GetString(CameraPosePrefsKeyPrefix + cameraPoseKey, string.Empty);
            if (string.IsNullOrEmpty(storedJson))
            {
                return false;
            }
            StoredCameraPose storedPose = new StoredCameraPose();
            try
            {
                JsonUtility.FromJsonOverwrite(storedJson, storedPose);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (storedPose.distance <= 0f)
            {
                return false;
            }
            pose = new PreviewCameraPose
            {
                yawDegrees = storedPose.yawDegrees,
                pitchDegrees = storedPose.pitchDegrees,
                distance = storedPose.distance,
                focus = storedPose.focus
            };
            return true;
        }

        public static void SaveCameraPose(string cameraPoseKey, in PreviewCameraPose pose)
        {
            if (string.IsNullOrEmpty(cameraPoseKey))
            {
                return;
            }
            StoredCameraPose storedPose = new StoredCameraPose
            {
                yawDegrees = pose.yawDegrees,
                pitchDegrees = pose.pitchDegrees,
                distance = pose.distance,
                focus = pose.focus
            };
            EditorPrefs.SetString(CameraPosePrefsKeyPrefix + cameraPoseKey, JsonUtility.ToJson(storedPose));
        }
    }
}
