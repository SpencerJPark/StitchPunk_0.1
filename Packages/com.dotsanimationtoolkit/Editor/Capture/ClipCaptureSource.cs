// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Captures one clip of a clip set on a rig through its own ClipPreviewController.</summary>
    public sealed class ClipCaptureSource : ICaptureSource
    {
        private readonly ClipSetAsset clipSet;
        private readonly RigAsset rig;
        private readonly ClipAsset clip;
        private readonly ClipPreviewController previewController;
        private bool isDisposed;

        public ClipCaptureSource(ClipSetAsset clipSet, RigAsset rig, ClipAsset clip)
        {
            this.clipSet = clipSet;
            this.rig = rig;
            this.clip = clip;
            this.previewController = new ClipPreviewController();

            if (rig != null)
            {
                this.previewController.SetRig(rig);
                this.previewController.SetSkinnedSource(rig.sourcePrefab);
            }

            if (clipSet != null)
            {
                this.previewController.SetClipSet(clipSet);
            }
        }

        public string CaptureName => this.clip == null ? "Capture" : SanitiseFileStem(this.clip.name);

        public string CameraPoseKey => "Clip." + GuidOf(this.clip) + "." + GuidOf(this.rig);

        public string NotReadyReason => this.clipSet != null && this.rig != null && this.clip != null
            ? null
            : "Choose a clip set, a rig and a clip to capture.";

        public float DurationSeconds => this.clip == null ? 0f : Mathf.Max(ClipAsset.MinimumDuration, this.clip.duration);

        public IPreviewCameraRig CameraRig => this.previewController;

        public PreviewCameraPose CaptureCameraPose()
        {
            return this.previewController.CapturePose();
        }

        public void RestoreCameraPose(in PreviewCameraPose pose)
        {
            this.previewController.RestorePose(in pose);
        }

        public void PoseAt(float seconds)
        {
            if (this.NotReadyReason != null || this.isDisposed)
            {
                return;
            }

            this.previewController.SamplePose(this.clip.Id.Value, Mathf.Clamp01(seconds / this.DurationSeconds));
        }

        public Texture RenderFrame(int pixelWidth, int pixelHeight, CaptureBackgroundMode background, Color backgroundColour)
        {
            if (this.NotReadyReason != null || this.isDisposed)
            {
                return null;
            }

            return this.previewController.RenderCaptureFrame(pixelWidth, pixelHeight, background == CaptureBackgroundMode.Transparent, backgroundColour);
        }

        public void Dispose()
        {
            if (this.isDisposed)
            {
                return;
            }

            this.isDisposed = true;
            this.previewController.Dispose();
        }

        private static string SanitiseFileStem(string name)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            char[] characters = name.ToCharArray();

            for (int characterIndex = 0; characterIndex < characters.Length; characterIndex++)
            {
                char currentCharacter = characters[characterIndex];
                bool isInvalid = currentCharacter == ' ' || Array.IndexOf(invalidCharacters, currentCharacter) >= 0;

                if (isInvalid)
                {
                    characters[characterIndex] = '_';
                }
            }

            return new string(characters);
        }

        private static string GuidOf(UnityEngine.Object asset)
        {
            return asset == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        }
    }
}
