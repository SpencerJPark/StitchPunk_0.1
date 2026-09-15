// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Renders a capture one frame per EditorApplication.update tick, writing PNGs or collecting GIF frames.</summary>
    public sealed class FrameCaptureRunner
    {
        private ICaptureSource activeSource;
        private CaptureSettings workingSettings;
        private Action<int, int> progressCallback;
        private Action<string> finishedCallback;
        private string outputFolder;
        private Texture2D frameTexture;
        private RenderTexture temporaryRenderTarget;
        private readonly List<string> writtenFilePaths = new List<string>();
        private readonly List<Color32[]> gifFrames = new List<Color32[]>();

        private bool isRunning;
        private bool wasCancelled;
        private int frameCount;
        private int framesCaptured;

        public bool IsRunning { get { return isRunning; } }
        public bool WasCancelled { get { return wasCancelled; } }
        public int FrameCount { get { return frameCount; } }
        public int FramesCaptured { get { return framesCaptured; } }
        public IReadOnlyList<string> WrittenFilePaths { get { return writtenFilePaths; } }

        public void Start(ICaptureSource source, CaptureSettings settings, Action<int, int> progress, Action<string> finished)
        {
            if (isRunning)
            {
                throw new InvalidOperationException("FrameCaptureRunner is already running a capture.");
            }

            string notReadyReason = source == null ? "no capture source is available." : source.NotReadyReason;
            if (notReadyReason != null)
            {
                finished?.Invoke("Cannot capture: " + notReadyReason);
                return;
            }

            activeSource = source;
            progressCallback = progress;
            finishedCallback = finished;

            workingSettings = settings.Clone();
            workingSettings.ClampToValidRanges();
            if (string.IsNullOrEmpty(workingSettings.captureName))
            {
                workingSettings.captureName = source.CaptureName;
            }

            outputFolder = workingSettings.ResolvedOutputFolder;
            PngSequenceWriter.EnsureOutputFolder(outputFolder);

            frameCount = workingSettings.FrameCountFor(source.DurationSeconds);
            framesCaptured = 0;
            wasCancelled = false;
            writtenFilePaths.Clear();
            gifFrames.Clear();

            bool transparentBackground = workingSettings.background == CaptureBackgroundMode.Transparent;
            frameTexture = new Texture2D(workingSettings.width, workingSettings.height, transparentBackground ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            frameTexture.hideFlags = HideFlags.HideAndDontSave;
            // sRGB read/write so the bytes written match what the preview viewport shows (drive-verified).
            temporaryRenderTarget = RenderTexture.GetTemporary(workingSettings.width, workingSettings.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

            isRunning = true;
            EditorApplication.update += Tick;
        }

        public void Cancel()
        {
            if (isRunning)
            {
                Finish(true, null);
            }
        }

        private void Tick()
        {
            try
            {
                float poseSeconds = workingSettings.SecondsAtFrame(framesCaptured, activeSource.DurationSeconds);
                activeSource.PoseAt(poseSeconds);
                Texture renderedTexture = activeSource.RenderFrame(workingSettings.width, workingSettings.height, workingSettings.background, workingSettings.backgroundColour);
                if (renderedTexture == null)
                {
                    Finish(false, "Capture failed: the source returned no rendered frame.");
                    return;
                }

                // The source owns renderedTexture; copy it out before it is reused or destroyed underneath us.
                Graphics.Blit(renderedTexture, temporaryRenderTarget);
                RenderTexture previousActiveRenderTexture = RenderTexture.active;
                RenderTexture.active = temporaryRenderTarget;
                frameTexture.ReadPixels(new Rect(0, 0, workingSettings.width, workingSettings.height), 0, 0, false);
                frameTexture.Apply(false);
                RenderTexture.active = previousActiveRenderTexture;

                if (workingSettings.format == CaptureOutputFormat.PngSequence)
                {
                    string writtenFramePath = PngSequenceWriter.WriteFile(outputFolder, PngSequenceWriter.FrameFileName(workingSettings.captureName, framesCaptured + 1), frameTexture.EncodeToPNG());
                    writtenFilePaths.Add(writtenFramePath);
                }
                else
                {
                    gifFrames.Add(frameTexture.GetPixels32());
                }

                framesCaptured++;
                progressCallback?.Invoke(framesCaptured, frameCount);

                if (framesCaptured == frameCount)
                {
                    Finish(false, null);
                }
            }
            catch (Exception tickException)
            {
                Debug.LogException(tickException);
                Finish(false, "Capture failed: " + tickException.Message);
            }
        }

        // Runs exactly once per capture: normal completion, Cancel(), and a mid-capture error all funnel through here.
        private void Finish(bool cancelled, string errorMessage)
        {
            EditorApplication.update -= Tick;

            if (temporaryRenderTarget != null)
            {
                RenderTexture.ReleaseTemporary(temporaryRenderTarget);
                temporaryRenderTarget = null;
            }
            if (frameTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(frameTexture);
                frameTexture = null;
            }

            wasCancelled = cancelled;
            bool completedNormally = errorMessage == null && !cancelled;
            string summaryMessage;

            if (errorMessage != null)
            {
                summaryMessage = errorMessage;
            }
            else if (completedNormally)
            {
                if (workingSettings.format == CaptureOutputFormat.Gif)
                {
                    string writtenGifPath = PngSequenceWriter.WriteFile(outputFolder, PngSequenceWriter.GifFileName(workingSettings.captureName), GifEncoding.Encode(gifFrames, workingSettings.width, workingSettings.height, workingSettings.GifFrameDelayCentiseconds));
                    writtenFilePaths.Add(writtenGifPath);
                    summaryMessage = "Captured " + framesCaptured + " frames to " + outputFolder + "/" + workingSettings.captureName + ".gif.";
                }
                else
                {
                    summaryMessage = "Captured " + framesCaptured + " frames to " + outputFolder + ".";
                }
            }
            else
            {
                if (workingSettings.format == CaptureOutputFormat.Gif)
                {
                    summaryMessage = "Cancelled; a GIF is written only when every frame is captured.";
                }
                else
                {
                    summaryMessage = "Cancelled after " + framesCaptured + " of " + frameCount + " frames; " + framesCaptured + " files kept in " + outputFolder + ".";
                }
            }

            if (writtenFilePaths.Count > 0)
            {
                PngSequenceWriter.RefreshAndApplyStillImportSettings(writtenFilePaths, workingSettings.background == CaptureBackgroundMode.Transparent);
            }

            gifFrames.Clear();
            isRunning = false;
            finishedCallback?.Invoke(summaryMessage);
        }
    }
}
