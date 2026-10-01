using System;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

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

        // The classes Unity puts on Play/Pause/Step. They carry the strip's 4px corner radius and
        // padding; both end caps because this element stands alone rather than inside a group.
        // They do NOT carry the background — that was measured, and comes from somewhere scoped to
        // the PlayMode element — so the fill is copied from the neighbour instead.
        private const string ButtonStripClassName = "unity-editor-toolbar__button-strip-element";
        private const string ButtonStripLeftClassName = "unity-editor-toolbar__button-strip-element--left";
        private const string ButtonStripRightClassName = "unity-editor-toolbar__button-strip-element--right";

        // Unity's highlight blue, the same token the style guide uses for selection. Applied directly
        // because checking the toggle does not tint it on its own.
        private static readonly Color ActiveTintColor = new Color(0.173f, 0.365f, 0.529f, 1f);
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
                RefreshToolbarElementChrome();
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
            RefreshToolbarElementChrome();
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.ExitingPlayMode)
            {
                IsCaptureSessionActive = false;
            }

            RefreshToolbarElementChrome();
        }

        /// Gives the element the chrome its neighbours have, and tints it while a capture session is
        /// running. Both have to be reapplied on every domain reload: the toolbar rebuilds the element
        /// from the factory each time, and the factory cannot reach the VisualElement it produces.
        /// Returns true once the element is wearing a definite fill, so the caller knows whether to
        /// keep retrying: resolved styles read as transparent until the toolbar has laid out, and a
        /// colour copied at that moment would be invisible.
        public static bool RefreshToolbarElementChrome()
        {
            try
            {
                VisualElement toggleElement = FindToolbarToggleElement();
                if (toggleElement == null)
                {
                    return false;
                }

                AddClassIfMissing(toggleElement, ButtonStripClassName);
                AddClassIfMissing(toggleElement, ButtonStripLeftClassName);
                AddClassIfMissing(toggleElement, ButtonStripRightClassName);

                if (IsCaptureSessionActive)
                {
                    toggleElement.style.backgroundColor = new StyleColor(ActiveTintColor);
                    return true;
                }

                // Copied from Play rather than hardcoded, so this tracks the skin and any future
                // change Unity makes to its own toolbar instead of drifting away from it.
                Color neighbourFill;
                if (!TryReadPlayButtonFill(out neighbourFill))
                {
                    return false;
                }

                toggleElement.style.backgroundColor = new StyleColor(neighbourFill);
                return true;
            }
            catch (Exception)
            {
                // Toolbar internals are not public API; a rename must cost the tint, not an exception
                // on every domain reload.
                return false;
            }
        }

        private static void AddClassIfMissing(VisualElement element, string className)
        {
            if (!element.ClassListContains(className))
            {
                element.AddToClassList(className);
            }
        }

        /// Finds a neighbour by what it looks like rather than by name: Unity's play overlay element
        /// is called "PlayMode" while its registered path is "Play Mode Controls", and matching on the
        /// path found nothing at all. Any strip element outside this one will do, and taking the first
        /// with a visible fill also skips the ones that have not laid out yet.
        private static bool TryReadPlayButtonFill(out Color fill)
        {
            fill = default(Color);
            VisualElement ownOverlay = FindToolbarOverlayElement(ToolbarElementPath);
            VisualElement toolbarRoot = FindToolbarRootElement();
            if (toolbarRoot == null)
            {
                return false;
            }

            return TryFindStripFillOutside(toolbarRoot, ownOverlay, ref fill);
        }

        private static bool TryFindStripFillOutside(VisualElement element, VisualElement excludedSubtree, ref Color fill)
        {
            if (element == excludedSubtree)
            {
                return false;
            }

            if (element.ClassListContains(ButtonStripClassName))
            {
                Color resolvedFill = element.resolvedStyle.backgroundColor;
                if (resolvedFill.a > 0f)
                {
                    fill = resolvedFill;
                    return true;
                }
            }

            for (int childIndex = 0; childIndex < element.childCount; childIndex++)
            {
                if (TryFindStripFillOutside(element[childIndex], excludedSubtree, ref fill))
                {
                    return true;
                }
            }

            return false;
        }

        private static VisualElement FindToolbarRootElement()
        {
            MethodInfo windowGetter = typeof(MainToolbar).GetMethod(
                "get_window", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (windowGetter == null)
            {
                return null;
            }

            EditorWindow toolbarWindow = windowGetter.Invoke(null, null) as EditorWindow;
            return toolbarWindow == null ? null : toolbarWindow.rootVisualElement;
        }

        private static VisualElement FindToolbarToggleElement()
        {
            MethodInfo windowGetter = typeof(MainToolbar).GetMethod(
                "get_window", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (windowGetter == null)
            {
                return null;
            }

            EditorWindow toolbarWindow = windowGetter.Invoke(null, null) as EditorWindow;
            if (toolbarWindow == null || toolbarWindow.rootVisualElement == null)
            {
                return null;
            }

            VisualElement overlayElement = FindDescendantByName(toolbarWindow.rootVisualElement, ToolbarElementPath);
            return overlayElement == null ? null : FindDescendantByTypeName(overlayElement, "EditorToolbarToggle");
        }

        /// The overlay wrapper is named after the element's registered path, which is how the two
        /// toolbar elements are told apart in the tree.
        private static VisualElement FindToolbarOverlayElement(string elementPath)
        {
            MethodInfo windowGetter = typeof(MainToolbar).GetMethod(
                "get_window", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (windowGetter == null)
            {
                return null;
            }

            EditorWindow toolbarWindow = windowGetter.Invoke(null, null) as EditorWindow;
            if (toolbarWindow == null || toolbarWindow.rootVisualElement == null)
            {
                return null;
            }

            return FindDescendantByName(toolbarWindow.rootVisualElement, elementPath);
        }

        private static VisualElement FindDescendantByName(VisualElement parent, string elementName)
        {
            if (parent.name == elementName)
            {
                return parent;
            }

            for (int childIndex = 0; childIndex < parent.childCount; childIndex++)
            {
                VisualElement found = FindDescendantByName(parent[childIndex], elementName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static VisualElement FindDescendantByTypeName(VisualElement parent, string typeName)
        {
            if (parent.GetType().Name == typeName)
            {
                return parent;
            }

            for (int childIndex = 0; childIndex < parent.childCount; childIndex++)
            {
                VisualElement found = FindDescendantByTypeName(parent[childIndex], typeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
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
            // The poll runs on every domain reload, not only the first: placement is a one-time
            // concern but the chrome is rebuilt with the element and has to be reapplied each time.
            placementAttemptsRemaining = PlacementAttemptLimit;
            EditorApplication.update += TryPlaceToolbarElementUntilToolbarExists;
        }

        private const int PlacementAttemptLimit = 600;

        private static void TryPlaceToolbarElementUntilToolbarExists()
        {
            placementAttemptsRemaining--;

            bool chromeApplied = RefreshToolbarElementChrome();
            bool placed = TryPlaceToolbarElementBesidePlayControls();
            if ((!placed || !chromeApplied) && placementAttemptsRemaining > 0)
            {
                return;
            }

            EditorApplication.update -= TryPlaceToolbarElementUntilToolbarExists;
        }

        /// MainToolbar exposes only Refresh publicly, and Overlay.DockAfter is internal, so placing
        /// the button means reflection. It is wrapped because an internal rename in a future Editor
        /// must cost a misplaced button, not an exception on every domain reload.
        private static bool TryPlaceToolbarElementBesidePlayControls()
        {
            string preferenceKey = FirstRunRevealPreferenceKeyPrefix
                + Application.dataPath.GetHashCode().ToString("X8", CultureInfo.InvariantCulture);
            if (EditorPrefs.GetBool(preferenceKey, false))
            {
                // Already placed once; moving it again would undo a drag the owner made deliberately.
                return true;
            }

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

                EditorPrefs.SetBool(preferenceKey, true);
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
