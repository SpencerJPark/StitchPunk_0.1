using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class SaveThumbnailCapture : MonoBehaviour
{
    public const int ThumbnailWidth = 320;
    public const int ThumbnailHeight = 180;

    static SaveThumbnailCapture instance;

    readonly List<Action<byte[]>> pendingCallbacks = new List<Action<byte[]>>();
    bool captureRoutineRunning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void SubscribeThumbnailWriterToSaveStarted()
    {
        // Unsubscribe first: with domain reload disabled this runs again on the same static event.
        SaveNotifications.SaveStarted -= WriteThumbnailForStartedSave;
        SaveNotifications.SaveStarted += WriteThumbnailForStartedSave;
    }

    static void WriteThumbnailForStartedSave(string saveId, SaveKind kind)
    {
        CaptureNextFrame(thumbnailPng =>
        {
            if (thumbnailPng != null)
            {
                SaveSlotStore.WriteFileAtomically(saveId, SaveSlotStore.ThumbnailFileName, thumbnailPng);
            }
        });
    }

    public static void CaptureNextFrame(Action<byte[]> onCaptured)
    {
        if (onCaptured == null)
        {
            return;
        }

        if (instance == null)
        {
            GameObject host = new GameObject("SaveThumbnailCapture");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SaveThumbnailCapture>();
        }

        instance.pendingCallbacks.Add(onCaptured);
        if (!instance.captureRoutineRunning)
        {
            instance.captureRoutineRunning = true;
            instance.StartCoroutine(instance.CaptureAtEndOfFrame());
        }
    }

    IEnumerator CaptureAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();

        // Requests arriving after this point wait for the next frame.
        List<Action<byte[]>> callbacksToInvoke = new List<Action<byte[]>>(pendingCallbacks);
        pendingCallbacks.Clear();
        captureRoutineRunning = false;

        byte[] pngBytes = null;
        try
        {
            pngBytes = CaptureDownsampledPng();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[SaveThumbnailCapture] Capture failed: " + exception.Message);
        }

        for (int callbackIndex = 0; callbackIndex < callbacksToInvoke.Count; callbackIndex++)
        {
            try
            {
                callbacksToInvoke[callbackIndex](pngBytes);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    // ScreenCapture grabs the final frame, so the UI layer cannot be excluded.
    static byte[] CaptureDownsampledPng()
    {
        Texture2D fullFrame = null;
        Texture2D thumbnail = null;
        RenderTexture downsampleTarget = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            fullFrame = ScreenCapture.CaptureScreenshotAsTexture();
            downsampleTarget = RenderTexture.GetTemporary(ThumbnailWidth, ThumbnailHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(fullFrame, downsampleTarget);
            RenderTexture.active = downsampleTarget;
            thumbnail = new Texture2D(ThumbnailWidth, ThumbnailHeight, TextureFormat.RGB24, false);
            thumbnail.ReadPixels(new Rect(0f, 0f, ThumbnailWidth, ThumbnailHeight), 0, 0);
            thumbnail.Apply(false);
            return ImageConversion.EncodeToPNG(thumbnail);
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (downsampleTarget != null)
            {
                RenderTexture.ReleaseTemporary(downsampleTarget);
            }
            if (fullFrame != null)
            {
                Destroy(fullFrame);
            }
            if (thumbnail != null)
            {
                Destroy(thumbnail);
            }
        }
    }
}
