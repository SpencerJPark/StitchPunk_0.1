using UnityEngine;

namespace PlaytestCopilot
{
    /// Push-to-talk substitute for developers whose keyboard record key collides with a game
    /// binding: a bottom-right screen button that opens the marker on press, closes it on release.
    public sealed class PlaytestOnScreenRecordButton : MonoBehaviour
    {
        private const float ButtonWidthPixels = 140f;
        private const float ButtonHeightPixels = 48f;
        private const float MarginPixels = 16f;
        private const float LevelMeterHeightPixels = 6f;
        private const float LevelMeterGapPixels = 2f;

        private bool isPressed;

        public bool IsVisible { get; set; } = true;
        public PlaytestVoiceCaptureController VoiceController { get; set; }
        public PlaytestMicrophoneRecorder Microphone { get; set; }

        private void OnGUI()
        {
            if (!IsVisible)
            {
                return;
            }

            Rect buttonRect = new Rect(Screen.width - ButtonWidthPixels - MarginPixels,
                Screen.height - ButtonHeightPixels - MarginPixels, ButtonWidthPixels, ButtonHeightPixels);

            bool markerIsOpen = VoiceController != null && VoiceController.IsMarkerOpen;

            if (markerIsOpen)
            {
                DrawLevelMeter(buttonRect);
            }

            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = markerIsOpen ? Color.red : previousBackgroundColor;

            // GUI.Button only fires on mouse-up, which would open and close a marker in the same
            // frame; GUI.RepeatButton reports true for every frame the mouse stays down.
            bool pressedThisFrame = GUI.RepeatButton(buttonRect, markerIsOpen ? "Recording" : "Hold to Record");

            GUI.backgroundColor = previousBackgroundColor;

            HandlePressStateChange(pressedThisFrame);
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

        private void DrawLevelMeter(Rect buttonRect)
        {
            if (Microphone == null)
            {
                return;
            }

            float inputLevel01 = Mathf.Clamp01(Microphone.CurrentInputLevel);
            Rect meterBackgroundRect = new Rect(buttonRect.x, buttonRect.y - LevelMeterHeightPixels - LevelMeterGapPixels,
                buttonRect.width, LevelMeterHeightPixels);
            Rect meterFillRect = new Rect(meterBackgroundRect.x, meterBackgroundRect.y,
                meterBackgroundRect.width * inputLevel01, meterBackgroundRect.height);

            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(meterBackgroundRect, Texture2D.whiteTexture);
            GUI.color = Color.green;
            GUI.DrawTexture(meterFillRect, Texture2D.whiteTexture);
            GUI.color = previousColor;
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
