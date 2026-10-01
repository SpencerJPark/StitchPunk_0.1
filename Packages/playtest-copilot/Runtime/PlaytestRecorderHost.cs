using UnityEngine;

namespace PlaytestCopilot
{
    /// What the Editor hands the host at play start, so the Runtime assembly never has to read
    /// Editor settings itself.
    public struct PlaytestRecorderConfiguration
    {
        public PlaytestVoiceMode VoiceMode;
        public KeyCode RecordKey;
        public KeyCode AnnotateKey;
        public float VoiceActivityThreshold;
        public float SilenceHangSeconds;
        public bool CaptureGameObjectState;
        public bool ShowOnScreenRecordButton;
        public string AudioFileAbsolutePath;
    }

    /// The composition root for a capture session: one hidden, undestroyable GameObject that owns
    /// every recorder and ticks them in a fixed order. Nothing else creates these components, and
    /// nothing in the package looks them up by type.
    public sealed class PlaytestRecorderHost : MonoBehaviour
    {
        public static PlaytestRecorderHost Instance { get; private set; }

        public PlaytestMicrophoneRecorder MicrophoneRecorder { get; private set; }
        public PlaytestVoiceCaptureController VoiceCapture { get; private set; }
        public PlaytestGameObjectStateBackend StateBackend { get; private set; }
        public PlaytestPointerTracker PointerTracker { get; private set; }
        public PlaytestAnnotationOverlay AnnotationOverlay { get; private set; }
        public PlaytestCaptureHud CaptureHud { get; private set; }

        private PlaytestRecorderConfiguration configuration;
        private bool annotateKeyWasDownLastFrame;

        public static PlaytestRecorderHost Create(PlaytestRecorderConfiguration configuration)
        {
            if (Instance != null)
            {
                return Instance;
            }

            GameObject hostObject = new GameObject("PlaytestCopilotRecorderHost");
            hostObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(hostObject);

            PlaytestRecorderHost host = hostObject.AddComponent<PlaytestRecorderHost>();
            host.configuration = configuration;
            host.BuildRecorders();
            Instance = host;
            return host;
        }

        public static void DestroyInstance()
        {
            if (Instance == null)
            {
                return;
            }

            Instance.ShutDown();
            Destroy(Instance.gameObject);
            Instance = null;
        }

        private void BuildRecorders()
        {
            MicrophoneRecorder = gameObject.AddComponent<PlaytestMicrophoneRecorder>();

            VoiceCapture = gameObject.AddComponent<PlaytestVoiceCaptureController>();
            VoiceCapture.Microphone = MicrophoneRecorder;
            VoiceCapture.Mode = configuration.VoiceMode;
            VoiceCapture.RecordKey = configuration.RecordKey;
            VoiceCapture.VoiceActivityThreshold = configuration.VoiceActivityThreshold;
            if (configuration.SilenceHangSeconds > 0f)
            {
                VoiceCapture.SilenceHangSeconds = configuration.SilenceHangSeconds;
            }


            PointerTracker = gameObject.AddComponent<PlaytestPointerTracker>();
            PointerTracker.CaptureCamera = Camera.main;

            if (configuration.CaptureGameObjectState)
            {
                StateBackend = gameObject.AddComponent<PlaytestGameObjectStateBackend>();
            }

            AnnotationOverlay = gameObject.AddComponent<PlaytestAnnotationOverlay>();

            CaptureHud = gameObject.AddComponent<PlaytestCaptureHud>();
            CaptureHud.VoiceController = VoiceCapture;
            CaptureHud.Microphone = MicrophoneRecorder;
            CaptureHud.AnnotationOverlay = AnnotationOverlay;
            CaptureHud.RecordKey = configuration.RecordKey;
            CaptureHud.AnnotateKey = configuration.AnnotateKey;
            CaptureHud.IsVisible = configuration.ShowOnScreenRecordButton;
            CaptureHud.FrameCaptureRequested += CaptureFrameWithoutDrawing;


            PlaytestCaptureBus.MarkerEnriching += EnrichMarker;
            MicrophoneRecorder.BeginCapture();
        }

        private void ShutDown()
        {
            PlaytestCaptureBus.MarkerEnriching -= EnrichMarker;
            if (CaptureHud != null)
            {
                CaptureHud.FrameCaptureRequested -= CaptureFrameWithoutDrawing;
            }

            if (VoiceCapture != null && VoiceCapture.IsMarkerOpen)
            {
                VoiceCapture.EndMarkerManually();
            }

            if (MicrophoneRecorder != null && !string.IsNullOrEmpty(configuration.AudioFileAbsolutePath))
            {
                MicrophoneRecorder.EndCaptureAndWriteWav(configuration.AudioFileAbsolutePath);
            }
        }

        /// Fills in what the voice controller cannot know: which objects the remark was about, and
        /// what the game looked like at that moment. Runs before any writer sees the marker.
        private void EnrichMarker(PlaytestMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            if (StateBackend != null)
            {
                marker.State = StateBackend.CaptureSnapshotFields();
            }

            PlaytestReferenceResolver.ResolveRequest request = new PlaytestReferenceResolver.ResolveRequest();
            request.CircledRegions = marker.CircledRegions;
            request.CaptureCamera = PointerTracker != null ? PointerTracker.CaptureCamera : Camera.main;
            request.ObjectUnderCursor = PointerTracker != null ? PointerTracker.ObjectUnderCursor : null;
            request.ObjectAtCameraCenter = PointerTracker != null ? PointerTracker.ObjectAtCameraCenter : null;
            request.LastInteractedObject = PointerTracker != null ? PointerTracker.LastInteractedObject : null;
            request.TranscriptText = string.IsNullOrEmpty(marker.TranscriptText) ? marker.TypedNote : marker.TranscriptText;
            request.PlayerTransform = StateBackend != null ? StateBackend.PlayerTransform : null;
            request.NearbyRadius = StateBackend != null ? StateBackend.NearbyRadius : 20f;
            request.MaxResults = 5;
            // EditorSelection stays null here: the Runtime assembly cannot see UnityEditor.Selection.
            // The Editor bootstrap supplies it through PlaytestEditorSelectionProbe when present.
            request.EditorSelection = EditorSelectionProvider != null ? EditorSelectionProvider() : null;

            marker.References = PlaytestReferenceResolver.Resolve(request);
        }

        /// The Capture button: the frame alone, no pause and no drawing. It rides the same bus event
        /// as a drawn annotation with only the frame filled in, so the writer needs no new path.
        private void CaptureFrameWithoutDrawing()
        {
            StartCoroutine(CaptureFrameAtEndOfFrame());
        }

        private System.Collections.IEnumerator CaptureFrameAtEndOfFrame()
        {
            // CaptureScreenshotAsTexture outside end-of-frame returns a black or torn texture.
            yield return new WaitForEndOfFrame();

            Texture2D frameTexture = ScreenCapture.CaptureScreenshotAsTexture();
            PlaytestAnnotationCapture capture = new PlaytestAnnotationCapture();
            capture.MarkerId = PlaytestMarkerId.For(VoiceCapture != null ? VoiceCapture.CompletedMarkerCount : 0);
            capture.Time = PlaytestSessionClock.Now;
            capture.FramePng = frameTexture.EncodeToPNG();
            PlaytestCaptureBus.RaiseAnnotationCaptured(capture);
            Destroy(frameTexture);
        }

        /// Set by the Editor bootstrap so the resolver can use the Editor's current selection, one
        /// of the six signals, without the Runtime assembly referencing UnityEditor.
        public static System.Func<GameObject> EditorSelectionProvider { get; set; }

        private void Update()
        {
            if (MicrophoneRecorder != null)
            {
                MicrophoneRecorder.TickCapture();
            }

            if (PointerTracker != null)
            {
                PointerTracker.TickCapture();
            }

            if (StateBackend != null)
            {
                StateBackend.TickCapture(Time.unscaledDeltaTime);
            }

            TickAnnotateKey();

            if (VoiceCapture != null)
            {
                VoiceCapture.TickCapture(IsRecordKeyHeld());
            }
        }

        private bool IsRecordKeyHeld()
        {
            if (AnnotationOverlay != null && AnnotationOverlay.IsOpen)
            {
                // Typing a note in the overlay must not be read as holding push-to-talk.
                return false;
            }

            try
            {
                return Input.GetKey(configuration.RecordKey);
            }
            catch (System.InvalidOperationException)
            {
                // The project disabled legacy input entirely; the on-screen button is the fallback.
                return false;
            }
        }

        private void TickAnnotateKey()
        {
            if (AnnotationOverlay == null)
            {
                return;
            }

            bool annotateKeyIsDown;
            try
            {
                annotateKeyIsDown = Input.GetKey(configuration.AnnotateKey);
            }
            catch (System.InvalidOperationException)
            {
                return;
            }

            if (annotateKeyIsDown && !annotateKeyWasDownLastFrame && !AnnotationOverlay.IsOpen)
            {
                AnnotationOverlay.Open(PlaytestMarkerId.For(VoiceCapture != null ? VoiceCapture.CompletedMarkerCount : 0));
            }

            annotateKeyWasDownLastFrame = annotateKeyIsDown;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                PlaytestCaptureBus.MarkerEnriching -= EnrichMarker;
                Instance = null;
            }
        }
    }
}
