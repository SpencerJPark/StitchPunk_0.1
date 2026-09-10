// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Brings the right window forward when the user moves between animating and authoring.
    /// Docking is the fix, focusing only the mechanism — once the window is docked in the Scene
    /// view's tab group, focusing either one brings it forward through Unity's own layout system.
    /// </summary>
    public static class ClipEditorDocking
    {
        // State carried across the one re-creation needed to dock a floating window. Unity does not
        // expose docking an existing floating window, so it is closed and reopened docked instead,
        // and this is what makes the reopened window feel like the same one.
        public sealed class CarriedState
        {
            public UnityEngine.Object clipSet;
            public UnityEngine.Object selectedClip;

            // A RigAsset rather than the prefab it used to carry: the Rig Hierarchy pane's field now picks the
            // rig, and the rig itself says which prefab the preview loads.
            public UnityEngine.Object rig;
            public float playheadTime;
            public bool rigEditMode;

            // Which tab was showing, as its underlying int. This type is deliberately free of the
            // window's own types — it is the handover between an instance being destroyed and one
            // that does not exist yet — and an int crosses that gap without dragging the enum's
            // declaring file into every consumer of this one.
            public int tab;
            public readonly List<string> selectedNames = new List<string>();
        }

        // Not a LoadWindowLayout swap: that destroys and recreates every editor window, including
        // this one's Persistent-allocator registry blob, for a smaller-blast-radius bring-forward.
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

        // The window types the Clip Editor prefers to dock beside. The Scene view, since the two are
        // alternatives never wanted at the same instant — sharing one tab group makes "switch to
        // prefab mode" a tab change rather than a window arrangement.
        public static Type[] PreferredDockNeighbours()
        {
            return new Type[] { typeof(SceneView) };
        }

        // Brings the prefab-authoring surface forward: the hierarchy, then the Scene view, in that
        // order — the last call also takes keyboard focus, which belongs to the Scene view.
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

        // Unity's hierarchy window type, whatever it is called in this version. Resolved from the
        // open windows rather than hard-coded, since Unity 6.5 opens HierarchyWindow where earlier
        // versions opened SceneHierarchyWindow.
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
