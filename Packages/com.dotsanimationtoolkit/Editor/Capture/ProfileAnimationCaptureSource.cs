// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Captures one profile animation at one facing through an ActorPreviewComposer and its own ClipPreviewController.</summary>
    public sealed class ProfileAnimationCaptureSource : ICaptureSource
    {
        private readonly ActorProfileAsset profileAsset;
        private readonly uint animationKey;
        private readonly Direction facing;
        private readonly ActorAnimationDefinition animationDefinition;
        private readonly ClipPreviewController previewController;
        private readonly ActorPreviewComposer previewComposer;
        private bool isDisposed;

        public ProfileAnimationCaptureSource(ActorProfileAsset profile, uint animationKey, Direction facing)
        {
            this.profileAsset = profile;
            this.animationKey = animationKey;
            this.facing = facing;
            this.animationDefinition = FindAnimationDefinition(profile, animationKey);

            this.previewController = new ClipPreviewController();
            this.previewComposer = new ActorPreviewComposer();

            if (profile != null && profile.rig != null)
            {
                this.previewController.SetRig(profile.rig);
                this.previewController.SetSkinnedSource(profile.rig.sourcePrefab);
                this.previewComposer.SetProfile(profile, this.previewController);
            }
        }

        private static ActorAnimationDefinition FindAnimationDefinition(ActorProfileAsset profile, uint animationKeyToFind)
        {
            if (profile == null || profile.layers == null)
            {
                return null;
            }

            for (int layerIndex = 0; layerIndex < profile.layers.Count; layerIndex++)
            {
                ActorLayerDefinition layerDefinition = profile.layers[layerIndex];
                if (layerDefinition == null || layerDefinition.animations == null)
                {
                    continue;
                }

                for (int animationIndex = 0; animationIndex < layerDefinition.animations.Count; animationIndex++)
                {
                    ActorAnimationDefinition candidateDefinition = layerDefinition.animations[animationIndex];
                    if (candidateDefinition != null && candidateDefinition.animationKey == animationKeyToFind)
                    {
                        return candidateDefinition;
                    }
                }
            }

            return null;
        }

        public string CaptureName
        {
            get
            {
                AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
                string resolvedName = registry != null ? registry.FindName(this.animationKey) : null;
                string fallbackStem = (this.profileAsset != null ? this.profileAsset.name : "Profile") + "_" + this.animationKey.ToString("X8");
                string stem = resolvedName ?? fallbackStem;
                return SanitiseFileStem(stem) + "_" + this.facing.ToString();
            }
        }

        public string CameraPoseKey
        {
            get
            {
                string assetPath = this.profileAsset != null ? AssetDatabase.GetAssetPath(this.profileAsset) : string.Empty;
                string assetGuid = string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
                return "Profile." + assetGuid + "." + this.animationKey.ToString("X8");
            }
        }

        public string NotReadyReason
        {
            get
            {
                if (this.profileAsset == null)
                {
                    return "Choose a profile to capture.";
                }

                if (this.profileAsset.rig == null)
                {
                    return "The profile has no rig.";
                }

                if (this.animationDefinition == null || this.animationDefinition.clip == null)
                {
                    return "Choose an animation from the profile.";
                }

                return null;
            }
        }

        public float DurationSeconds
        {
            get
            {
                if (this.animationDefinition == null || this.animationDefinition.clip == null)
                {
                    return 0f;
                }

                return this.animationDefinition.clip.duration / Mathf.Max(0.0001f, Mathf.Abs(this.animationDefinition.speed));
            }
        }

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
            if (this.isDisposed || this.NotReadyReason != null || !this.previewComposer.IsCreated)
            {
                return;
            }

            this.previewComposer.Reset();
            this.previewComposer.Facing = this.facing;
            this.previewComposer.PlayAnimation(this.animationKey);

            int matchingLayerIndex = -1;
            for (int layerIndex = 0; layerIndex < this.previewComposer.LayerCount; layerIndex++)
            {
                if (this.previewComposer.LayerAnimationKey(layerIndex) == this.animationKey)
                {
                    matchingLayerIndex = layerIndex;
                    break;
                }
            }

            if (matchingLayerIndex < 0)
            {
                this.previewComposer.Tick(0f, this.previewController);
                return;
            }

            // Finish the blend-in so the capture shows the animation, not the crossfade from the starter.
            float blendDuration = this.previewComposer.Layer(matchingLayerIndex).blendDuration;
            if (blendDuration > 0f)
            {
                this.previewComposer.Tick(blendDuration, this.previewController);
            }

            // Layer time is seconds on the clip timeline, advanced at speed.
            this.previewComposer.SetLayerTime(matchingLayerIndex, seconds * this.animationDefinition.speed);
            this.previewComposer.Tick(0f, this.previewController);
        }

        public Texture RenderFrame(int pixelWidth, int pixelHeight, CaptureBackgroundMode background, Color backgroundColour)
        {
            if (this.isDisposed || this.NotReadyReason != null)
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
            this.previewComposer.Dispose();
            this.previewController.Dispose();
        }

        private static string SanitiseFileStem(string fileStem)
        {
            char[] invalidFileNameCharacters = Path.GetInvalidFileNameChars();
            char[] sanitisedCharacters = fileStem.ToCharArray();
            for (int characterIndex = 0; characterIndex < sanitisedCharacters.Length; characterIndex++)
            {
                char currentCharacter = sanitisedCharacters[characterIndex];
                if (currentCharacter == ' ' || Array.IndexOf(invalidFileNameCharacters, currentCharacter) >= 0)
                {
                    sanitisedCharacters[characterIndex] = '_';
                }
            }

            return new string(sanitisedCharacters);
        }
    }
}
