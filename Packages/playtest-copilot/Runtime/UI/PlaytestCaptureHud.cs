using UnityEngine;

namespace PlaytestCopilot
{
    /// The capture block: a draggable card over the game view carrying the recording status, the
    /// live microphone level, and the three actions a session needs.
    ///
    /// Styled to Docs/AnimationToolkit/EditorStyleGuide.md through PlaytestHudStyle — one surface,
    /// Unity's neutrals, one primary action, and colour only where it means status. The full-width
    /// red banner this replaced broke that last rule, which is why it is now a tone dot and a line.
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

        public bool IsVisible { get; set; } = true;
        public PlaytestVoiceCaptureController VoiceController { get; set; }
        public PlaytestMicrophoneRecorder Microphone { get; set; }
        public PlaytestAnnotationOverlay AnnotationOverlay { get; set; }
        public KeyCode RecordKey { get; set; } = KeyCode.BackQuote;
        public KeyCode AnnotateKey { get; set; } = KeyCode.F2;

        /// Raised when the owner presses Capture; a subscriber elsewhere owns writing the frame, so
        /// this stays harmless while nothing is listening.
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

            float scale = Scale;
            Rect cardRect = ComputeCardRect(scale);
            HandleCardDragging(cardRect, scale);
            cardRect = ComputeCardRect(scale);

            PlaytestHudStyle.DrawSurface(cardRect);
            DrawStatusRow(cardRect, scale);
            DrawLevelMeter(cardRect, scale);
            DrawActionRow(cardRect, scale);
            DrawHintRow(cardRect, scale);
        }

        private float CardWidth(float scale)
        {
            return Mathf.Min(Screen.width - 2f * PlaytestHudStyle.Inset * scale, 320f * scale);
        }

        private float CardHeight(float scale)
        {
            float inset = PlaytestHudStyle.Inset * scale;
            return inset
                + PlaytestHudStyle.ControlHeight * scale
                + PlaytestHudStyle.SpaceTiny * scale
                + PlaytestHudStyle.MeterHeight * scale
                + PlaytestHudStyle.SpaceSmall * scale
                + PlaytestHudStyle.PrimaryHeight * scale
                + PlaytestHudStyle.SpaceTiny * scale
                + PlaytestHudStyle.ControlHeight * scale
                + inset * 0.5f;
        }

        private Rect ComputeCardRect(float scale)
        {
            EnsureCardPositionInitialised(scale);
            return new Rect(barTopLeft.x, barTopLeft.y, CardWidth(scale), CardHeight(scale));
        }

        private void EnsureCardPositionInitialised(float scale)
        {
            if (hasBarPosition)
            {
                return;
            }

            if (PlayerPrefs.HasKey(HudPositionXPrefKey) && PlayerPrefs.HasKey(HudPositionYPrefKey))
            {
                barTopLeft = new Vector2(
                    PlayerPrefs.GetFloat(HudPositionXPrefKey) * Screen.width,
                    PlayerPrefs.GetFloat(HudPositionYPrefKey) * Screen.height);
            }
            else
            {
                float inset = PlaytestHudStyle.Inset * scale;
                barTopLeft = new Vector2(
                    (Screen.width - CardWidth(scale)) * 0.5f,
                    Screen.height - CardHeight(scale) - inset);
            }

            hasBarPosition = true;
            ClampCardPosition(scale);
        }

        /// The status row doubles as the drag handle, so the card moves from its title the way a
        /// window does rather than from anywhere a stray click happens to land.
        private void DrawStatusRow(Rect cardRect, float scale)
        {
            float inset = PlaytestHudStyle.Inset * scale;
            bool markerIsOpen = VoiceController != null && VoiceController.IsMarkerOpen;

            float dotSize = 8f * scale;
            Rect dotRect = new Rect(
                cardRect.x + inset,
                cardRect.y + inset + (PlaytestHudStyle.ControlHeight * scale - dotSize) * 0.5f,
                dotSize,
                dotSize);

            // The dot pulses while recording so the card still reads as live at a glance.
            Color tone = PlaytestHudStyle.StatusIdle;
            if (markerIsOpen)
            {
                float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 3f));
                tone = new Color(PlaytestHudStyle.StatusRecording.r, PlaytestHudStyle.StatusRecording.g,
                    PlaytestHudStyle.StatusRecording.b, pulse);
            }

            PlaytestHudStyle.DrawStatusDot(dotRect, tone);

            Rect titleRect = new Rect(
                dotRect.xMax + PlaytestHudStyle.SpaceSmall * scale,
                cardRect.y + inset,
                cardRect.width - inset * 2f - dotSize,
                PlaytestHudStyle.ControlHeight * scale);
            GUI.Label(titleRect, markerIsOpen ? RecordingTitle() : "Playtest Copilot",
                PlaytestHudStyle.PaneTitle(scale));
        }

        private string RecordingTitle()
        {
            int noteNumber = VoiceController != null ? VoiceController.CompletedMarkerCount + 1 : 1;
            return "Recording note " + noteNumber;
        }

        private void DrawLevelMeter(Rect cardRect, float scale)
        {
            float inset = PlaytestHudStyle.Inset * scale;
            Rect meterRect = new Rect(
                cardRect.x + inset,
                cardRect.y + inset + PlaytestHudStyle.ControlHeight * scale + PlaytestHudStyle.SpaceTiny * scale,
                cardRect.width - inset * 2f,
                PlaytestHudStyle.MeterHeight * scale);

            PlaytestHudStyle.DrawSolid(meterRect, PlaytestHudStyle.FieldBackground);

            if (Microphone == null)
            {
                return;
            }

            float inputLevel01 = Mathf.Clamp01(Microphone.CurrentInputLevel);
            bool markerIsOpen = VoiceController != null && VoiceController.IsMarkerOpen;
            Rect fillRect = new Rect(meterRect.x, meterRect.y, meterRect.width * inputLevel01, meterRect.height);
            PlaytestHudStyle.DrawSolid(fillRect,
                markerIsOpen ? PlaytestHudStyle.MeterFill : PlaytestHudStyle.TextMuted);
        }

        private void DrawActionRow(Rect cardRect, float scale)
        {
            float inset = PlaytestHudStyle.Inset * scale;
            float gap = PlaytestHudStyle.SpaceTiny * scale;
            float rowY = ActionRowY(cardRect, scale);
            float rowHeight = PlaytestHudStyle.PrimaryHeight * scale;
            float available = cardRect.width - inset * 2f - gap * 2f;
            float recordWidth = available * 0.44f;
            float secondaryWidth = (available - recordWidth) * 0.5f;

            bool markerIsOpen = VoiceController != null && VoiceController.IsMarkerOpen;
            Rect recordRect = new Rect(cardRect.x + inset, rowY, recordWidth, rowHeight);

            // The one primary action on this surface. RepeatButton rather than Button, because the
            // marker must stay open for as long as it is held.
            Color recordFill = markerIsOpen ? PlaytestHudStyle.StatusRecording : PlaytestHudStyle.TextLabel;
            bool isHovered = recordRect.Contains(Event.current.mousePosition);
            PlaytestHudStyle.DrawSolid(recordRect, isHovered ? PlaytestHudStyle.Lighten(recordFill, 0.06f) : recordFill);
            GUI.Label(recordRect, markerIsOpen ? "Recording" : "Hold to Talk", PlaytestHudStyle.PrimaryButton(scale));
            bool pressedThisFrame = GUI.RepeatButton(recordRect, GUIContent.none, GUIStyle.none);
            HandlePressStateChange(pressedThisFrame);

            Rect circleRect = new Rect(recordRect.xMax + gap, rowY, secondaryWidth, rowHeight);
            if (PlaytestHudStyle.DrawFlatButton(circleRect, "Circle", PlaytestHudStyle.SecondaryButton(scale),
                PlaytestHudStyle.ButtonBackground, PlaytestHudStyle.Divider))
            {
                OpenAnnotationOverlay();
            }

            Rect captureRect = new Rect(circleRect.xMax + gap, rowY, secondaryWidth, rowHeight);
            if (PlaytestHudStyle.DrawFlatButton(captureRect, "Capture", PlaytestHudStyle.SecondaryButton(scale),
                PlaytestHudStyle.ButtonBackground, PlaytestHudStyle.Divider))
            {
                if (FrameCaptureRequested != null)
                {
                    FrameCaptureRequested();
                }
            }
        }

        private float ActionRowY(Rect cardRect, float scale)
        {
            float inset = PlaytestHudStyle.Inset * scale;
            return cardRect.y + inset
                + PlaytestHudStyle.ControlHeight * scale + PlaytestHudStyle.SpaceTiny * scale
                + PlaytestHudStyle.MeterHeight * scale + PlaytestHudStyle.SpaceSmall * scale;
        }

        private void DrawHintRow(Rect cardRect, float scale)
        {
            float inset = PlaytestHudStyle.Inset * scale;
            Rect hintRect = new Rect(
                cardRect.x + inset,
                ActionRowY(cardRect, scale) + PlaytestHudStyle.PrimaryHeight * scale + PlaytestHudStyle.SpaceTiny * scale,
                cardRect.width - inset * 2f,
                PlaytestHudStyle.ControlHeight * scale);
            GUI.Label(hintRect, RecordKey + " talk  ·  " + AnnotateKey + " draw  ·  drag the title to move",
                PlaytestHudStyle.MetaLabel(scale));
        }

        private void OpenAnnotationOverlay()
        {
            if (AnnotationOverlay == null || AnnotationOverlay.IsOpen)
            {
                return;
            }

            int markerIndex = VoiceController != null ? VoiceController.CompletedMarkerCount : 0;
            AnnotationOverlay.Open(PlaytestMarkerId.For(markerIndex));
        }

        /// Dragging starts only on the status row, never on a button, so a click on Circle or Capture
        /// can never be mistaken for the start of a drag.
        private void HandleCardDragging(Rect cardRect, float scale)
        {
            float inset = PlaytestHudStyle.Inset * scale;
            Rect dragHandleRect = new Rect(cardRect.x, cardRect.y, cardRect.width,
                inset + PlaytestHudStyle.ControlHeight * scale);
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.MouseDown && dragHandleRect.Contains(currentEvent.mousePosition))
            {
                isDragging = true;
                dragGrabOffset = currentEvent.mousePosition - new Vector2(cardRect.x, cardRect.y);
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && isDragging)
            {
                barTopLeft = currentEvent.mousePosition - dragGrabOffset;
                ClampCardPosition(scale);
                currentEvent.Use();
                return;
            }

            if (currentEvent.type == EventType.MouseUp && isDragging)
            {
                isDragging = false;
                SaveCardPosition();
                currentEvent.Use();
            }
        }

        /// Normalised so a resized game view keeps the card roughly where it was put instead of
        /// throwing it off the edge.
        private void SaveCardPosition()
        {
            PlayerPrefs.SetFloat(HudPositionXPrefKey, barTopLeft.x / Mathf.Max(1f, Screen.width));
            PlayerPrefs.SetFloat(HudPositionYPrefKey, barTopLeft.y / Mathf.Max(1f, Screen.height));
            PlayerPrefs.Save();
        }

        private void ClampCardPosition(float scale)
        {
            float width = CardWidth(scale);
            barTopLeft.x = Mathf.Clamp(barTopLeft.x, MinimumVisibleEdgePixels - width, Screen.width - MinimumVisibleEdgePixels);
            barTopLeft.y = Mathf.Clamp(barTopLeft.y, 0f, Screen.height - MinimumVisibleEdgePixels);
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
