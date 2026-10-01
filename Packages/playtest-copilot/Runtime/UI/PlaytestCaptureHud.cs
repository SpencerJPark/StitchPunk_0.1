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

        private bool isPressed;
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
            float barWidth = Mathf.Min(Screen.width - 32f * scale, 560f * scale);
            Rect barRect = new Rect((Screen.width - barWidth) * 0.5f, Screen.height - barHeight - 16f * scale, barWidth, barHeight);
            DrawSolid(barRect, new Color(0.08f, 0.08f, 0.09f, 0.82f));

            float padding = 12f * scale;
            float buttonWidth = 190f * scale;
            Rect buttonRect = new Rect(barRect.x + padding, barRect.y + padding, buttonWidth, barRect.height - padding * 2f);

            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = markerIsOpen ? new Color(0.9f, 0.25f, 0.25f) : previousBackgroundColor;
            buttonStyle.fontSize = Mathf.RoundToInt(15f * scale);

            // GUI.Button only fires on mouse-up, which would open and close a marker in the same
            // frame; GUI.RepeatButton reports true for every frame the mouse stays down.
            bool pressedThisFrame = GUI.RepeatButton(buttonRect, markerIsOpen ? "Recording..." : "Hold to Record", buttonStyle);
            GUI.backgroundColor = previousBackgroundColor;
            HandlePressStateChange(pressedThisFrame);

            float meterX = buttonRect.xMax + padding;
            float meterWidth = barRect.xMax - padding - meterX;
            DrawLevelMeter(new Rect(meterX, barRect.y + padding, meterWidth, 20f * scale), markerIsOpen);

            hintLabelStyle.fontSize = Mathf.RoundToInt(13f * scale);
            Rect hintRect = new Rect(meterX, barRect.y + padding + 24f * scale, meterWidth, barRect.height - padding - 24f * scale);
            GUI.Label(hintRect, "Hold [" + RecordKey + "] to talk     Press [" + AnnotateKey + "] to pause and draw", hintLabelStyle);
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
