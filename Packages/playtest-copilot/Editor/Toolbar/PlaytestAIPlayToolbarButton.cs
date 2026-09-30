using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlaytestCopilot.Editor
{
    // Front door of the product: the AI-Play button that sits next to Unity's own Play button.
    // Toggling it flips a session-scoped capture flag that a later file's session lifecycle reads.
    public static class PlaytestAIPlayToolbarButton
    {
        private const string CaptureSessionActiveSessionStateKey = "PlaytestCopilot.CaptureSessionActive";
        private const string ActiveButtonUssClassName = "playtest-ai-play-toolbar-button--active";
        private const string ToggleButtonTooltipText =
            "Play with Playtest Copilot capture on: records state, notes and video for this session.";

        private static readonly Color ActiveButtonBackgroundColor = new Color(0.24f, 0.48f, 0.90f, 1f);

        private static EditorToolbarButton aiPlayButtonElement;

        static PlaytestAIPlayToolbarButton()
        {
            // Cleared here rather than in ToggleAIPlay so a normal Play press (which never calls
            // ToggleAIPlay) also leaves the flag false once the editor returns from any play session.
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        public static bool IsCaptureSessionActive
        {
            get { return SessionState.GetBool(CaptureSessionActiveSessionStateKey, false); }
            private set { SessionState.SetBool(CaptureSessionActiveSessionStateKey, value); }
        }

        [MainToolbarElement("PlaytestCopilot/AIPlay", defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 100)]
        public static VisualElement CreateAIPlayToolbarElement()
        {
            aiPlayButtonElement = new EditorToolbarButton(PlaytestIconLoader.LoadAIPlayIcon(), ToggleAIPlay)
            {
                tooltip = ToggleButtonTooltipText,
            };
            RefreshButtonHighlight();
            return aiPlayButtonElement;
        }

        public static void ToggleAIPlay()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
                return;
            }

            IsCaptureSessionActive = true;
            RefreshButtonHighlight();
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            IsCaptureSessionActive = false;
            RefreshButtonHighlight();
        }

        private static void RefreshButtonHighlight()
        {
            if (aiPlayButtonElement == null)
            {
                return;
            }

            bool isActive = IsCaptureSessionActive;
            aiPlayButtonElement.EnableInClassList(ActiveButtonUssClassName, isActive);
            aiPlayButtonElement.style.backgroundColor = isActive
                ? new StyleColor(ActiveButtonBackgroundColor)
                : new StyleColor(StyleKeyword.Null);
        }
    }
}
