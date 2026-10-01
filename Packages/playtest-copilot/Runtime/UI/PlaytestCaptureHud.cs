using UnityEngine;

namespace PlaytestCopilot
{
    /// The only thing on screen during an AI Play session: a recording indicator loud enough to see
    /// at a glance, a live microphone level, and the two keys the session depends on. It doubles as
    /// the push-to-talk substitute for developers whose record key collides with a game binding.
    ///
    /// Everything is sized from a scale factor rather than in fixed pixels, because a game view on a
    /// high-DPI display made the previous fixed 140x48 panel too small to read.
    public sealed class PlaytestCaptureHud : MonoBehaviour
    {
        private const float ReferenceScreenHeight = 900f;
        private const float MaximumScale = 3f;
        private const float MinimumVisibleEdgePixels = 40f;
        private const string HudPositionXPrefKey = "PlaytestCopilot.HudX";
        private const string HudPositionYPrefKey = "PlaytestCopilot.HudY";

        private bool isPressed;
        private bool isDragging;
        private bool hasBarPosition;
        private Vector2 barTopLeft;
        private Vector2 dragGrabOffset;
        private GUIStyle recordingLabelStyle;
        private GUIStyle hintLabelStyle;
        private GUIStyle buttonStyle;
        private Texture2D solidTexture;

        public bool IsVisible { get; set; } = true;
        public PlaytestVoiceCaptureController VoiceController { get; set; }
        public PlaytestMicrophoneRecorder Microphone { get; set; }
        public PlaytestAnnotationOverlay AnnotationOverlay { get; set; }
        public KeyCode RecordKey { get; set; } = KeyCode.BackQuote;
        public KeyCode AnnotateKey { get; set; } = KeyCode.F2;

        /// Raised when the owner presses the Capture button; a subscriber elsewhere owns writing the
        /// frame to disk, so this stays harmless while nothing is listening.
        public event System.Action FrameCaptureRequested;

        private float Scale
        {
            get { return Mathf.Clamp(Screen.height / ReferenceScreenHeight, 1f, MaximumScale); }
        }

        private void OnGUI()
        {
            // While the draw overlay is up it owns the screen; two IMGUI layers fighting for the
            // same clicks is how a circle turns into a button press.
            if (!IsVisible || (AnnotationOverlay != null && AnnotationOverlay.IsOpen))
            {
                return;
            }

            EnsureStyles();

            bool markerIsOpen = VoiceController != null && VoiceController.IsMarkerOpen;
            float scale = Scale;

            DrawRecordingBanner(markerIsOpen, scale);
            DrawControlBar(markerIsOpen, scale);
        }

        /// A wide red banner across the top. The complaint this answers is that the old indicator was
        /// too small to notice, so this is deliberately hard to miss while a note is being recorded.
        private void DrawRecordingBanner(bool markerIsOpen, float scale)
        {
            if (!markerIsOpen)
            {
                return;
            }

            float bannerHeight = 44f * scale;
            Rect bannerRect = new Rect(0f, 0f, Screen.width, bannerHeight);
            DrawSolid(bannerRect, new Color(0.75f, 0.12f, 0.12f, 0.92f));

            // The dot pulses so the banner still reads as live when the level meter is near silent.
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 3f));
            float dotSize = 16f * scale;
            Rect dotRect = new Rect(18f * scale, (bannerHeight - dotSize) * 0.5f, dotSize, dotSize);
            DrawSolid(dotRect, new Color(1f, 1f, 1f, pulse));

            recordingLabelStyle.fontSize = Mathf.RoundToInt(20f * scale);
            Rect labelRect = new Rect(dotRect.xMax + 12f * scale, 0f, Screen.width, bannerHeight);
            GUI.Label(labelRect, "RECORDING NOTE " + CompletedMarkerCountText(), recordingLabelStyle);
        }

        private string CompletedMarkerCountText()
        {
            if (VoiceController == null)
            {
                return string.Empty;
            }

            return "#" + (VoiceController.CompletedMarkerCount + 1);
        }

        private void DrawControlBar(bool markerIsOpen, float scale)
        {
            float barHeight = 64f * scale;
            float barWidth = Mathf.Min(Screen.width - 32f * scale, 760f * scale);
            EnsureBarPositionInitialized(barWidth, barHeight, scale);

            float padding = 12f * scale;
            float recordButtonWidth = 190f * scale;
            float iconButtonWidth = 86f * scale;

            Rect barRect = new Rect(barTopLeft.x, barTopLeft.y, barWidth, barHeight);
            Rect recordButtonRect;
            Rect drawButtonRect;
            Rect captureButtonRect;
            ComputeBarButtonRects(barRect, padding, recordButtonWidth, iconButtonWidth, out recordButtonRect, out drawButtonRect, out captureButtonRect);

            HandleBarDragging(barRect, recordButtonRect, drawButtonRect, captureButtonRect, barWidth, barHeight);

            // Re-derive from the (possibly just-moved) top-left so the bar tracks the cursor the same
            // frame it is dragged instead of lagging one OnGUI pass behind.
            barRect = new Rect(barTopLeft.x, barTopLeft.y, barWidth, barHeight);
            ComputeBarButtonRects(barRect, padding, recordButtonWidth, iconButtonWidth, out recordButtonRect, out drawButtonRect, out captureButtonRect);

            DrawSolid(barRect, new Color(0.08f, 0.08f, 0.09f, 0.82f));

            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = markerIsOpen ? new Color(0.9f, 0.25f, 0.25f) : previousBackgroundColor;
            buttonStyle.fontSize = Mathf.RoundToInt(15f * scale);

            // GUI.Button only fires on mouse-up, which would open and close a marker in the same
            // frame; GUI.RepeatButton reports true for every frame the mouse stays down.
            bool pressedThisFrame = GUI.RepeatButton(recordButtonRect, markerIsOpen ? "Recording..." : "Hold to Record", buttonStyle);
            GUI.backgroundColor = previousBackgroundColor;
            HandlePressStateChange(pressedThisFrame);

            if (GUI.Button(drawButtonRect, "Draw", buttonStyle))
            {
                OpenAnnotationOverlay();
            }

            if (GUI.Button(captureButtonRect, "Capture", buttonStyle))
            {
                FrameCaptureRequested?.Invoke();
            }

            float meterX = captureButtonRect.xMax + padding;
            float meterWidth = barRect.xMax - padding - meterX;
            DrawLevelMeter(new Rect(meterX, barRect.y + padding, meterWidth, 20f * scale), markerIsOpen);

            hintLabelStyle.fontSize = Mathf.RoundToInt(13f * scale);
            Rect hintRect = new Rect(meterX, barRect.y + padding + 24f * scale, meterWidth, barRect.height - padding - 24f * scale);
            GUI.Label(hintRect, "Hold [" + RecordKey + "] or Record to talk     Draw to annotate [" + AnnotateKey + "]     Capture grabs a frame     Drag bar to move", hintLabelStyle);
        }

        private void ComputeBarButtonRects(Rect barRect, float padding, float recordButtonWidth, float iconButtonWidth, out Rect recordButtonRect, out Rect drawButtonRect, out Rect captureButtonRect)
        {
            recordButtonRect = new Rect(barRect.x + padding, barRect.y + padding, recordButtonWidth, barRect.height - padding * 2f);
            drawButtonRect = new Rect(recordButtonRect.xMax + padding, barRect.y + padding, iconButtonWidth, barRect.height - padding * 2f);
            captureButtonRect = new Rect(drawButtonRect.xMax + padding, barRect.y + padding, iconButtonWidth, barRect.height - padding * 2f);
        }

        private void OpenAnnotationOverlay()
        {
            if (AnnotationOverlay == null || AnnotationOverlay.IsOpen)
            {
                return;
            }

            int completedMarkerCount = VoiceController != null ? VoiceController.CompletedMarkerCount : 0;
            AnnotationOverlay.Open(PlaytestMarkerId.For(completedMarkerCount));
        }

        // Click-and-drag anywhere on the bar background moves it; a mouse-down that lands inside one
        // of the three button rects is excluded so it reaches the button instead of starting a drag.
        private void HandleBarDragging(Rect barRect, Rect recordButtonRect, Rect drawButtonRect, Rect captureButtonRect, float barWidth, float barHeight)
        {
            Event currentEvent = Event.current;
            Vector2 mousePosition = currentEvent.mousePosition;

            if (currentEvent.type == EventType.MouseDown && barRect.Contains(mousePosition)
                && !recordButtonRect.Contains(mousePosition) && !drawButtonRect.Contains(mousePosition) && !captureButtonRect.Contains(mousePosition))
            {
                isDragging = true;
                dragGrabOffset = mousePosition - barTopLeft;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && isDragging)
            {
                barTopLeft = ClampBarPosition(mousePosition - dragGrabOffset, barWidth, barHeight);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseUp && isDragging)
            {
                isDragging = false;
                SaveBarPosition();
                currentEvent.Use();
            }
        }

        private Vector2 ClampBarPosition(Vector2 desiredTopLeft, float barWidth, float barHeight)
        {
            float minimumX = MinimumVisibleEdgePixels - barWidth;
            float maximumX = Screen.width - MinimumVisibleEdgePixels;
            float minimumY = MinimumVisibleEdgePixels - barHeight;
            float maximumY = Screen.height - MinimumVisibleEdgePixels;

            float clampedX = Mathf.Clamp(desiredTopLeft.x, minimumX, maximumX);
            float clampedY = Mathf.Clamp(desiredTopLeft.y, minimumY, maximumY);
            return new Vector2(clampedX, clampedY);
        }

        // Runs once per enable: loads the normalised saved position, or falls back to the original
        // bottom-centre placement when the owner has never dragged the bar before.
        private void EnsureBarPositionInitialized(float barWidth, float barHeight, float scale)
        {
            if (hasBarPosition)
            {
                return;
            }

            hasBarPosition = true;

            if (PlayerPrefs.HasKey(HudPositionXPrefKey) && PlayerPrefs.HasKey(HudPositionYPrefKey))
            {
                float normalizedX = PlayerPrefs.GetFloat(HudPositionXPrefKey);
                float normalizedY = PlayerPrefs.GetFloat(HudPositionYPrefKey);
                barTopLeft = ClampBarPosition(new Vector2(normalizedX * Screen.width, normalizedY * Screen.height), barWidth, barHeight);
                return;
            }

            barTopLeft = new Vector2((Screen.width - barWidth) * 0.5f, Screen.height - barHeight - 16f * scale);
        }

        // Normalised so a resized game view keeps the bar roughly where the owner put it instead of
        // throwing it off screen; saved only on mouse-up, not every drag frame.
        private void SaveBarPosition()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            PlayerPrefs.SetFloat(HudPositionXPrefKey, barTopLeft.x / Screen.width);
            PlayerPrefs.SetFloat(HudPositionYPrefKey, barTopLeft.y / Screen.height);
            PlayerPrefs.Save();
        }

        private void DrawLevelMeter(Rect meterRect, bool markerIsOpen)
        {
            DrawSolid(meterRect, new Color(0f, 0f, 0f, 0.75f));

            if (Microphone == null)
            {
                hintLabelStyle.fontSize = Mathf.RoundToInt(13f * Scale);
                GUI.Label(meterRect, "  no microphone", hintLabelStyle);
                return;
            }

            float inputLevel01 = Mathf.Clamp01(Microphone.CurrentInputLevel);
            Rect fillRect = new Rect(meterRect.x, meterRect.y, meterRect.width * inputLevel01, meterRect.height);
            DrawSolid(fillRect, markerIsOpen ? new Color(0.35f, 0.9f, 0.35f) : new Color(0.45f, 0.45f, 0.48f));
        }

        private void HandlePressStateChange(bool pressedThisFrame)
        {
            if (VoiceController == null)
            {
                isPressed = false;
                return;
            }

            if (pressedThisFrame && !isPressed)
            {
                VoiceController.BeginMarkerManually();
            }
            else if (!pressedThisFrame && isPressed)
            {
                VoiceController.EndMarkerManually();
            }

            isPressed = pressedThisFrame;
        }

        private void DrawSolid(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, solidTexture);
            GUI.color = previousColor;
        }

        private void EnsureStyles()
        {
            if (solidTexture == null)
            {
                solidTexture = Texture2D.whiteTexture;
            }

            if (recordingLabelStyle == null)
            {
                recordingLabelStyle = new GUIStyle(GUI.skin.label);
                recordingLabelStyle.alignment = TextAnchor.MiddleLeft;
                recordingLabelStyle.fontStyle = FontStyle.Bold;
                recordingLabelStyle.normal.textColor = Color.white;
            }

            if (hintLabelStyle == null)
            {
                hintLabelStyle = new GUIStyle(GUI.skin.label);
                hintLabelStyle.alignment = TextAnchor.MiddleLeft;
                hintLabelStyle.normal.textColor = new Color(0.82f, 0.82f, 0.85f);
            }

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.fontStyle = FontStyle.Bold;
            }
        }

        // A game (or its own pause menu) can steal focus mid-press; without this the marker
        // would stay open until the button happens to be pressed and released again.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus || !isPressed)
            {
                return;
            }

            HandlePressStateChange(false);
        }

        private void OnDisable()
        {
            if (!isPressed)
            {
                return;
            }

            HandlePressStateChange(false);
        }
    }
}
