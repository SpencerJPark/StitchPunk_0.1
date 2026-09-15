// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Captures a cutscene posed in its open scene through a hidden utility camera on an orbit rig.</summary>
    public sealed class CutsceneCaptureSource : ICaptureSource
    {
        private const string CaptureCameraName = "CaptureCutsceneCamera (hidden)";

        private readonly CutsceneAsset cutscene;
        private readonly CutscenePreviewController previewController;
        private readonly PreviewOrbitCameraRig cameraRig;
        private readonly string captureName;
        private readonly string cameraPoseKey;
        private readonly float durationSeconds;

        private RenderTexture renderTexture;
        private Camera captureCamera;
        private bool hasEnteredPreviewOnce;
        private bool hasRestoredCameraPoseFromCaller;

        public CutsceneCaptureSource(CutsceneAsset cutscene)
        {
            this.cutscene = cutscene;
            previewController = new CutscenePreviewController();
            cameraRig = new PreviewOrbitCameraRig();
            captureName = SanitizeFileName(cutscene != null ? cutscene.name : null);
            cameraPoseKey = cutscene != null
                ? "Cutscene." + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cutscene))
                : string.Empty;
            durationSeconds = cutscene != null ? ComputeContentEndSeconds(cutscene) : 0f;

            // HideAndDontSave cameras survive a domain reload while this source does not, so a reload
            // between captures would otherwise leak one hidden camera per session.
            DestroyLeakedCaptureCameras();
        }

        public string CaptureName { get { return captureName; } }
        public string CameraPoseKey { get { return cameraPoseKey; } }
        public float DurationSeconds { get { return durationSeconds; } }
        public IPreviewCameraRig CameraRig { get { return cameraRig; } }

        public string NotReadyReason
        {
            get
            {
                if (cutscene == null)
                {
                    return "Choose a cutscene to capture.";
                }
                if (CutsceneSceneBinding.CurrentSceneGuid() != cutscene.sceneGuid)
                {
                    return "Open the cutscene's scene to capture it.";
                }
                return null;
            }
        }

        public PreviewCameraPose CaptureCameraPose()
        {
            return cameraRig.CapturePose();
        }

        public void RestoreCameraPose(in PreviewCameraPose pose)
        {
            cameraRig.RestorePose(pose);
            hasRestoredCameraPoseFromCaller = true;
        }

        public void PoseAt(float seconds)
        {
            if (NotReadyReason != null)
            {
                return;
            }
            EnsurePreviewEntered();
            previewController.ApplyPose(cutscene, seconds);
        }

        public Texture RenderFrame(int pixelWidth, int pixelHeight, CaptureBackgroundMode background, Color backgroundColour)
        {
            if (NotReadyReason != null || pixelWidth <= 0 || pixelHeight <= 0)
            {
                return null;
            }
            EnsurePreviewEntered();
            EnsureRenderResources(pixelWidth, pixelHeight);

            captureCamera.backgroundColor = background == CaptureBackgroundMode.Transparent
                ? new Color(0f, 0f, 0f, 0f)
                : backgroundColour;
            cameraRig.ApplyTo(captureCamera);

            // Transparent only clears the background colour behind the render; the scene's own opaque
            // geometry still draws on top exactly as it does with a solid background.
            UniversalRenderPipeline.SingleCameraRequest renderRequest =
                new UniversalRenderPipeline.SingleCameraRequest { destination = renderTexture };
            if (RenderPipeline.SupportsRenderRequest(captureCamera, renderRequest))
            {
                RenderPipeline.SubmitRenderRequest(captureCamera, renderRequest);
            }
            else
            {
                captureCamera.targetTexture = renderTexture;
                captureCamera.Render();
                captureCamera.targetTexture = null;
            }
            return renderTexture;
        }

        public void Dispose()
        {
            if (previewController.IsActive)
            {
                previewController.ExitPreview();
            }
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                renderTexture = null;
            }
            if (captureCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(captureCamera.gameObject);
                captureCamera = null;
            }
        }

        private void EnsurePreviewEntered()
        {
            if (hasEnteredPreviewOnce)
            {
                return;
            }
            hasEnteredPreviewOnce = true;
            previewController.EnterPreview(cutscene, CutsceneSceneBinding.CurrentSceneGuid());

            if (!hasRestoredCameraPoseFromCaller)
            {
                Bounds? boundObjectBounds = ComputeBoundObjectBounds();
                if (boundObjectBounds.HasValue)
                {
                    cameraRig.Frame(boundObjectBounds.Value);
                }
            }
        }

        private Bounds? ComputeBoundObjectBounds()
        {
            bool hasBounds = false;
            Bounds combinedBounds = default(Bounds);
            if (cutscene.slots != null)
            {
                for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
                {
                    CutsceneSlot slot = cutscene.slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }
                    GameObject boundObject = previewController.GetBoundObject(slot.SlotId);
                    if (boundObject == null)
                    {
                        continue;
                    }
                    Renderer[] boundRenderers = boundObject.GetComponentsInChildren<Renderer>();
                    for (int rendererIndex = 0; rendererIndex < boundRenderers.Length; rendererIndex++)
                    {
                        if (!hasBounds)
                        {
                            combinedBounds = boundRenderers[rendererIndex].bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            combinedBounds.Encapsulate(boundRenderers[rendererIndex].bounds);
                        }
                    }
                }
            }
            return hasBounds ? combinedBounds : (Bounds?)null;
        }

        private void EnsureRenderResources(int pixelWidth, int pixelHeight)
        {
            if (renderTexture == null || renderTexture.width != pixelWidth || renderTexture.height != pixelHeight)
            {
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                renderTexture = new RenderTexture(pixelWidth, pixelHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = "CutsceneCaptureRT",
                    hideFlags = HideFlags.HideAndDontSave
                };
                renderTexture.Create();
            }

            if (captureCamera == null)
            {
                GameObject cameraObject = new GameObject(CaptureCameraName)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                captureCamera = cameraObject.AddComponent<Camera>();
                captureCamera.enabled = false;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.cullingMask = ~0;
                captureCamera.fieldOfView = 45f;
                captureCamera.nearClipPlane = 0.05f;
                captureCamera.farClipPlane = 1000f;
            }
        }

        private static void DestroyLeakedCaptureCameras()
        {
            Camera[] allCameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int cameraIndex = 0; cameraIndex < allCameras.Length; cameraIndex++)
            {
                Camera candidateCamera = allCameras[cameraIndex];
                if (candidateCamera != null && candidateCamera.gameObject.name == CaptureCameraName)
                {
                    UnityEngine.Object.DestroyImmediate(candidateCamera.gameObject);
                }
            }
        }

        private static string SanitizeFileName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return "Cutscene";
            }
            char[] invalidFileNameCharacters = Path.GetInvalidFileNameChars();
            StringBuilder sanitizedNameBuilder = new StringBuilder(rawName.Length);
            for (int characterIndex = 0; characterIndex < rawName.Length; characterIndex++)
            {
                char currentCharacter = rawName[characterIndex];
                if (currentCharacter == ' ' || Array.IndexOf(invalidFileNameCharacters, currentCharacter) >= 0)
                {
                    sanitizedNameBuilder.Append('_');
                }
                else
                {
                    sanitizedNameBuilder.Append(currentCharacter);
                }
            }
            return sanitizedNameBuilder.Length > 0 ? sanitizedNameBuilder.ToString() : "Cutscene";
        }

        // Mirrors CutsceneEditorPanel.ComputeContentEndSeconds so a capture's default range matches
        // what the timeline shows; the panel's copy is private, so this one is kept in step by hand.
        private static float ComputeContentEndSeconds(CutsceneAsset cutsceneAsset)
        {
            float latestTimeSeconds = 1f;
            if (cutsceneAsset.slots != null)
            {
                for (int slotIndex = 0; slotIndex < cutsceneAsset.slots.Count; slotIndex++)
                {
                    CutsceneSlot slot = cutsceneAsset.slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }
                    if (slot.clipBlocks != null)
                    {
                        for (int clipBlockIndex = 0; clipBlockIndex < slot.clipBlocks.Count; clipBlockIndex++)
                        {
                            latestTimeSeconds = Mathf.Max(
                                latestTimeSeconds,
                                slot.clipBlocks[clipBlockIndex].start + slot.clipBlocks[clipBlockIndex].duration);
                        }
                    }
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, LatestTime(slot.transformKeys));
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, LatestTime(slot.facingKeys));
                    if (slot.partTracks != null)
                    {
                        for (int partTrackIndex = 0; partTrackIndex < slot.partTracks.Count; partTrackIndex++)
                        {
                            latestTimeSeconds = Mathf.Max(latestTimeSeconds, LatestTime(slot.partTracks[partTrackIndex].keys));
                        }
                    }
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, LatestTime(slot.attachMarkers));
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, LatestArrivalTime(slot.markKeys));
                }
            }
            latestTimeSeconds = Mathf.Max(latestTimeSeconds, LatestTime(cutsceneAsset.cameraLane?.keys));
            if (cutsceneAsset.cameraLane?.cutMarkers != null)
            {
                for (int cutMarkerIndex = 0; cutMarkerIndex < cutsceneAsset.cameraLane.cutMarkers.Count; cutMarkerIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, cutsceneAsset.cameraLane.cutMarkers[cutMarkerIndex].time);
                }
            }
            if (cutsceneAsset.events != null)
            {
                for (int eventIndex = 0; eventIndex < cutsceneAsset.events.Count; eventIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, cutsceneAsset.events[eventIndex].time);
                }
            }
            if (cutsceneAsset.holdMarkers != null)
            {
                for (int holdMarkerIndex = 0; holdMarkerIndex < cutsceneAsset.holdMarkers.Count; holdMarkerIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, cutsceneAsset.holdMarkers[holdMarkerIndex].time);
                }
            }
            return latestTimeSeconds;
        }

        private static float LatestTime(List<CutsceneTransformKey> keys)
        {
            float latestTimeSeconds = 0f;
            if (keys != null)
            {
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, keys[keyIndex].time);
                }
            }
            return latestTimeSeconds;
        }

        private static float LatestTime(List<CutsceneAttachMarker> markers)
        {
            float latestTimeSeconds = 0f;
            if (markers != null)
            {
                for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, markers[markerIndex].time);
                }
            }
            return latestTimeSeconds;
        }

        private static float LatestArrivalTime(List<CutsceneMarkKey> marks)
        {
            float latestTimeSeconds = 0f;
            if (marks != null)
            {
                for (int markIndex = 0; markIndex < marks.Count; markIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, CutsceneMarkMerge.ArrivalTime(marks[markIndex]));
                }
            }
            return latestTimeSeconds;
        }

        private static float LatestTime(List<CutsceneFacingKey> keys)
        {
            float latestTimeSeconds = 0f;
            if (keys != null)
            {
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, keys[keyIndex].time);
                }
            }
            return latestTimeSeconds;
        }

        private static float LatestTime(List<CutsceneCameraKey> keys)
        {
            float latestTimeSeconds = 0f;
            if (keys != null)
            {
                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    latestTimeSeconds = Mathf.Max(latestTimeSeconds, keys[keyIndex].time);
                }
            }
            return latestTimeSeconds;
        }
    }
}
