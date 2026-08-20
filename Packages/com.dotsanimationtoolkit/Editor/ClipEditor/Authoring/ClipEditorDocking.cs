// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Brings the right window forward when the user moves between animating and authoring.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Docking is the fix; focusing is only the mechanism.</strong> A floating window sits
    /// above the main window whatever has keyboard focus, so no amount of focusing the Scene view
    /// will get a floating Clip Editor out of the way — the user ends up dragging it, which is the
    /// complaint. Once the window is docked in the Scene view's tab group, focusing the Scene view
    /// puts the Clip Editor behind it automatically and focusing the Clip Editor brings it back.
    /// That is the whole swap, and it is Unity's own layout system doing it.
    /// </para>
    /// <para>
    /// <strong>Why not a layout swap.</strong> <c>EditorUtility.LoadWindowLayout</c> destroys and
    /// recreates every editor window, including this one. That would take the preview's render
    /// utility and its <c>Persistent</c>-allocator registry blob with it, along with the playhead
    /// and selection the round trip is supposed to preserve — and it would rearrange windows the
    /// user never asked this feature to touch. Sending one window behind another is a smaller
    /// action with a smaller blast radius, and it is the one that survives a failure gracefully:
    /// the worst case is a window that did not come forward, not a rebuilt editor.
    /// </para>
    /// </remarks>
    public static class ClipEditorDocking
    {
        /// <summary>
        /// State carried across the one re-creation needed to dock a floating window.
        /// </summary>
        /// <remarks>
        /// Docking an existing floating window is not something Unity exposes, so the window is
        /// closed and reopened asking to be docked. That loses the instance, hence this: the few
        /// values that make the window feel like the same window when it comes back.
        /// </remarks>
        public sealed class CarriedState
        {
            public UnityEngine.Object clipSet;
            public UnityEngine.Object selectedClip;
            public GameObject skinnedSource;
            public float playheadTime;
            public bool rigEditMode;
            public readonly List<string> selectedNames = new List<string>();
        }

        private static CarriedState pendingState;

        /// <summary>The state a reopened window should adopt, consumed once.</summary>
        public static CarriedState ConsumePendingState()
        {
            CarriedState state = pendingState;
            pendingState = null;
            return state;
        }

        /// <summary>Stashes state for the window that is about to be reopened docked.</summary>
        public static void SetPendingState(CarriedState state)
        {
            pendingState = state;
        }

        /// <summary>
        /// The window types the Clip Editor prefers to dock beside.
        /// </summary>
        /// <remarks>
        /// The Scene view first, and the whole point of that choice: the two are alternatives, never
        /// wanted at the same instant. Animating uses the Clip Editor's own viewport, and authoring
        /// structure uses the Scene view. Sharing one tab group makes "switch to prefab mode" a tab
        /// change rather than a window arrangement.
        /// </remarks>
        public static Type[] PreferredDockNeighbours()
        {
            return new Type[] { typeof(SceneView) };
        }

        /// <summary>
        /// Brings the prefab-authoring surface forward: the hierarchy, then the Scene view.
        /// </summary>
        /// <remarks>
        /// In that order deliberately. Each call brings its window to the front of whatever tab
        /// group it lives in, and the last one also takes keyboard focus — which belongs to the
        /// Scene view, because that is where the user is about to click and drag.
        /// </remarks>
        public static void FocusPrefabAuthoring()
        {
            Type hierarchyType = ResolveHierarchyWindowType();
            if (hierarchyType != null)
            {
                EditorWindow.FocusWindowIfItsOpen(hierarchyType);
            }

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Focus();
                return;
            }
            EditorWindow.FocusWindowIfItsOpen<SceneView>();
        }

        /// <summary>
        /// Unity's hierarchy window type, whatever it is called in this version.
        /// </summary>
        /// <remarks>
        /// Resolved from the open windows rather than hard-coded, because the type moved: Unity 6.5
        /// opens a <c>HierarchyWindow</c> where earlier versions opened a
        /// <c>SceneHierarchyWindow</c>, and both names still exist. Asking the editor what it
        /// actually has cannot be wrong about it, whereas a hard-coded name silently focuses nothing
        /// the day it changes again.
        /// </remarks>
        private static Type ResolveHierarchyWindowType()
        {
            EditorWindow[] openWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            for (int windowIndex = 0; windowIndex < openWindows.Length; windowIndex++)
            {
                EditorWindow window = openWindows[windowIndex];
                if (window == null)
                {
                    continue;
                }
                string typeName = window.GetType().Name;
                if (typeName == "HierarchyWindow" || typeName == "SceneHierarchyWindow")
                {
                    return window.GetType();
                }
            }

            // Nothing open: fall back to the historical name so the window can at least be created.
            return Type.GetType("UnityEditor.SceneHierarchyWindow,UnityEditor");
        }
    }
}
