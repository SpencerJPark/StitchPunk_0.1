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
        // Bumped when the first-run placement changes, so an Editor that already ran the old one
        // re-places the button once instead of keeping it where the previous version left it.
        private const string FirstRunRevealPreferenceKeyPrefix = "PlaytestCopilot.ToolbarPlaced2.";
        private const string PlayModeControlsElementPath = "Play Mode Controls";
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

        /// A newly registered main toolbar element is hidden *and* unplaced: it is an overlay, so the
        /// Editor's saved toolbar layout decides both. The layout predates this element, so
        /// `displayed` comes back false and the button never appears even though registration
        /// succeeded — and `defaultDockPosition` only applies the very first time a path registers,
        /// so once a layout has placed it, changing the attribute moves nothing.
        ///
        /// Both are therefore fixed here, exactly once per project, which leaves a later deliberate
        /// hide or drag (right-click the toolbar) alone.
        private static int placementAttemptsRemaining;

        [InitializeOnLoadMethod]
        private static void RevealToolbarElementOnFirstRun()
        {
            string preferenceKey = FirstRunRevealPreferenceKeyPrefix
                + Application.dataPath.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
            if (EditorPrefs.GetBool(preferenceKey, false))
            {
                return;
            }

            // The toolbar does not exist during InitializeOnLoad, and a single delayCall is too
            // early too — it fires before the toolbar window is built, finds no overlay, and the
            // placement is silently skipped for the whole session. Poll instead, briefly.
            placementAttemptsRemaining = PlacementAttemptLimit;
            EditorApplication.update += TryPlaceToolbarElementUntilToolbarExists;
        }

        private const int PlacementAttemptLimit = 600;

        private static void TryPlaceToolbarElementUntilToolbarExists()
        {
            placementAttemptsRemaining--;

            bool placed = TryPlaceToolbarElementBesidePlayControls();
            if (!placed && placementAttemptsRemaining > 0)
            {
                return;
            }

            EditorApplication.update -= TryPlaceToolbarElementUntilToolbarExists;
            if (placed)
            {
                string preferenceKey = FirstRunRevealPreferenceKeyPrefix
                    + Application.dataPath.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
                EditorPrefs.SetBool(preferenceKey, true);
            }
        }

        /// MainToolbar exposes only Refresh publicly, and Overlay.DockAfter is internal, so placing
        /// the button means reflection. It is wrapped because an internal rename in a future Editor
        /// must cost a misplaced button, not an exception on every domain reload.
        private static bool TryPlaceToolbarElementBesidePlayControls()
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

                Overlay aiPlayOverlay = FindOverlay(tryGetOverlay, ToolbarElementPath);
                if (aiPlayOverlay == null)
                {
                    return false;
                }

                aiPlayOverlay.displayed = true;

                // Docking after Play Mode Controls puts it beside Play/Pause/Step, which is the whole
                // point of the button — the Middle *section* is what the eye reads as "next to Play",
                // and the dock attribute alone cannot get it there once a layout exists.
                Overlay playModeOverlay = FindOverlay(tryGetOverlay, PlayModeControlsElementPath);
                if (playModeOverlay != null)
                {
                    MethodInfo dockAfter = typeof(Overlay).GetMethod(
                        "DockAfter",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new Type[] { typeof(Overlay) },
                        null);
                    if (dockAfter != null)
                    {
                        dockAfter.Invoke(aiPlayOverlay, new object[] { playModeOverlay });
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Overlay FindOverlay(MethodInfo tryGetOverlay, string elementPath)
        {
            object[] arguments = new object[] { elementPath, null };
            if (!(bool)tryGetOverlay.Invoke(null, arguments))
            {
                return null;
            }

            return arguments[1] as Overlay;
        }
    }
}
