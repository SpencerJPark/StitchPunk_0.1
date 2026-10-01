using System;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;

namespace PlaytestCopilot.Editor
{
    // Front door of the product: the AI Play control that sits next to Unity's own Play button.
    // Turning it on flips a session-scoped capture flag that PlaytestSessionBootstrap reads.
    public static class PlaytestAIPlayToolbarButton
    {
        public const string ToolbarElementPath = "PlaytestCopilot/AIPlay";

        private const string CaptureSessionActiveSessionStateKey = "PlaytestCopilot.CaptureSessionActive";
        private const string FirstRunRevealPreferenceKeyPrefix = "PlaytestCopilot.ToolbarRevealed.";
        private const string ToggleTooltipText =
            "Play with Playtest Copilot capture on: records voice, state and notes for this session.";
        private const string ToggleFallbackText = "AI Play";

        private static MainToolbarToggle aiPlayToggle;

        static PlaytestAIPlayToolbarButton()
        {
            // Cleared on ExitingPlayMode, not EnteredEditMode: exiting play mode triggers a domain
            // reload that rebuilds the toolbar element from this flag, and EnteredEditMode fires
            // after that rebuild — so clearing there would leave the control stuck visibly on.
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        public static bool IsCaptureSessionActive
        {
            get { return SessionState.GetBool(CaptureSessionActiveSessionStateKey, false); }
            private set { SessionState.SetBool(CaptureSessionActiveSessionStateKey, value); }
        }

        /// The method must return a concrete MainToolbarElement. Returning a VisualElement compiles
        /// cleanly but is rejected at load with "must return MainToolbarElementData", and the only
        /// symptom is a toolbar with no button on it.
        ///
        /// MainToolbarToggle rather than MainToolbarButton because its on state is exactly the "lit
        /// while recording" behaviour, drawn by the Editor's own toolbar styling. It exposes no
        /// settable value, so the flag is what persists and the element is seeded from it on every
        /// domain reload.
        /// Middle, not Left: Middle is the zone that holds Play/Pause/Step, which is where the spec
        /// asks for this. Left is the far-left tools zone. A dock default only applies the first time
        /// an element registers, so changing this later will not move an already-placed button.
        [MainToolbarElement(ToolbarElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 100)]
        public static MainToolbarElement CreateAIPlayToolbarElement()
        {
            aiPlayToggle = new MainToolbarToggle(BuildContent(), IsCaptureSessionActive, HandleToggleValueChanged);
            return aiPlayToggle;
        }

        public static void ToggleAIPlay()
        {
            HandleToggleValueChanged(!EditorApplication.isPlaying);
        }

        private static MainToolbarContent BuildContent()
        {
            Texture2D icon = PlaytestIconLoader.LoadAIPlayIcon();
            if (icon == null)
            {
                // Content with a null image renders as an invisible, unclickable gap, so fall back
                // to a label rather than leaving the user with nothing to press.
                return new MainToolbarContent(ToggleFallbackText, ToggleTooltipText);
            }

            return new MainToolbarContent(icon, ToggleTooltipText);
        }

        private static void HandleToggleValueChanged(bool shouldCapture)
        {
            if (!shouldCapture)
            {
                IsCaptureSessionActive = false;
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }

                return;
            }

            if (EditorApplication.isPlaying)
            {
                // Already playing without capture. Starting mid-session would record a session whose
                // beginning is missing, so this does nothing rather than producing a misleading one.
                return;
            }

            IsCaptureSessionActive = true;
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.ExitingPlayMode)
            {
                IsCaptureSessionActive = false;
            }
        }

        /// A newly registered main toolbar element is hidden: it is an overlay, and the Editor's saved
        /// toolbar layout predates it, so `displayed` comes back false and the button never appears
        /// even though registration succeeded. Reveal it exactly once per project, which leaves a
        /// later deliberate hide (right-click the toolbar) alone.
        [InitializeOnLoadMethod]
        private static void RevealToolbarElementOnFirstRun()
        {
            // The toolbar does not exist yet during InitializeOnLoad.
            EditorApplication.delayCall += TryRevealToolbarElementOnce;
        }

        private static void TryRevealToolbarElementOnce()
        {
            EditorApplication.delayCall -= TryRevealToolbarElementOnce;

            string preferenceKey = FirstRunRevealPreferenceKeyPrefix
                + Application.dataPath.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
            if (EditorPrefs.GetBool(preferenceKey, false))
            {
                return;
            }

            // Only record success: if the toolbar was not ready, the next domain reload retries.
            if (TrySetToolbarElementDisplayed())
            {
                EditorPrefs.SetBool(preferenceKey, true);
            }
        }

        /// MainToolbar exposes only Refresh publicly, so reaching the overlay means reflection. It is
        /// wrapped because an internal rename in a future Editor must cost a hidden button, not an
        /// exception on every domain reload.
        private static bool TrySetToolbarElementDisplayed()
        {
            try
            {
                MethodInfo tryGetOverlay = typeof(MainToolbar).GetMethod(
                    "TryGetOverlay",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (tryGetOverlay == null)
                {
                    return false;
                }

                object[] arguments = new object[] { ToolbarElementPath, null };
                if (!(bool)tryGetOverlay.Invoke(null, arguments))
                {
                    return false;
                }

                Overlay toolbarOverlay = arguments[1] as Overlay;
                if (toolbarOverlay == null)
                {
                    return false;
                }

                toolbarOverlay.displayed = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
