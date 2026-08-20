// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Guards the clip editor's dock layout (architecture section 7.1).
    /// </summary>
    /// <remarks>
    /// <strong>The element names are a contract between two files that no compiler checks.</strong>
    /// <c>ClipEditorWindow.cs</c> reaches into the cloned tree with <c>Q&lt;T&gt;(name)</c>, and a
    /// miss returns null rather than raising anything — so renaming an element in the UXML silently
    /// empties a pane and the window still opens looking almost right. That is the whole reason
    /// these two tests exist; everything else about the layout fails visibly on sight.
    /// </remarks>
    public sealed class ClipEditorLayoutTests
    {
        private const string LayoutAssetPath =
            "Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.uxml";

        /// <summary>
        /// Every element <c>ClipEditorWindow</c> resolves by name, in the order it binds them so a
        /// diff against <c>ClipEditorWindow.cs</c> reads straight down.
        /// </summary>
        private static readonly string[] RequiredElementNames = new string[]
        {
            "clip-editor-root", "clip-editor-toolbar",
            "clip-set-field", "new-clip-set-button",
            "play-toggle", "rewind-button", "time-label", "snap-toggle",
            "rig-edit-toggle", "billboard-preview-toggle",
            "frame-count-field", "skinned-source-field", "validation-badge-slot",
            "dock-vertical", "dock-columns", "dock-left", "dock-right",
            "clip-list-pane", "new-clip-button", "delete-clip-button", "clip-list",
            "hierarchy-pane", "hierarchy-empty-label", "hierarchy-tree",
            "add-socket-button", "billboard-root-button", "edit-prefab-button",
            "viewport-pane", "viewport-status", "viewport-image",
            "inspector-pane", "inspector-content",
            "timeline-pane", "timeline-status", "timeline-scroll", "timeline-row",
            "track-header-stack", "track-header-column", "lane-stack", "lane-column"
        };

        [Test]
        public void ClonedLayout_ContainsEverySlotTheWindowResolves()
        {
            VisualElement cloneTarget = CloneLayout();

            List<string> missingNames = new List<string>();
            foreach (string elementName in RequiredElementNames)
            {
                if (cloneTarget.Q<VisualElement>(elementName) == null)
                {
                    missingNames.Add(elementName);
                }
            }

            Assert.IsEmpty(
                missingNames,
                "ClipEditorWindow resolves these by name and would get null instead, which empties a " +
                "pane silently: " + string.Join(", ", missingNames));

            // Where the stylesheet lands is not obvious and it matters: a root-level <Style> in UXML
            // applies to the element cloned *into* — rootVisualElement for the window — not to the
            // cloned child. Every size in the window comes from it, so losing it is losing the layout.
            Assert.AreEqual(
                1, cloneTarget.styleSheets.count,
                "ClipEditorWindow.uxml must apply its stylesheet to the clone target.");
        }

        [Test]
        public void Dock_SplitsMatchTheOnesTheWindowPersists()
        {
            // The window restores each split by resolving its fixed pane by name. If an orientation
            // or a fixed-pane-index moved, it would store and restore the wrong pane, and the symptom
            // is a layout that drifts a little every time the window opens.
            VisualElement cloneTarget = CloneLayout();

            AssertSplit(cloneTarget, "dock-vertical", TwoPaneSplitViewOrientation.Vertical, "timeline-pane");
            AssertSplit(cloneTarget, "dock-columns", TwoPaneSplitViewOrientation.Horizontal, "dock-left");
            AssertSplit(cloneTarget, "dock-left", TwoPaneSplitViewOrientation.Vertical, "clip-list-pane");
            AssertSplit(cloneTarget, "dock-right", TwoPaneSplitViewOrientation.Horizontal, "inspector-pane");
        }

        private static VisualElement CloneLayout()
        {
            VisualTreeAsset layoutAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutAssetPath);
            Assert.IsNotNull(layoutAsset, "Missing layout asset: " + LayoutAssetPath);

            VisualElement cloneTarget = new VisualElement();
            layoutAsset.CloneTree(cloneTarget);
            return cloneTarget;
        }

        private static void AssertSplit(
            VisualElement cloneTarget, string splitName,
            TwoPaneSplitViewOrientation expectedOrientation, string expectedFixedPaneName)
        {
            TwoPaneSplitView splitView = cloneTarget.Q<TwoPaneSplitView>(splitName);
            Assert.IsNotNull(splitView, splitName + " must be a TwoPaneSplitView.");
            Assert.AreEqual(
                expectedOrientation, splitView.orientation,
                splitName + " must be a " + expectedOrientation.ToString() + " split.");

            // Indexed through the children rather than through TwoPaneSplitView.fixedPane, which is
            // only populated once the split has laid itself out — and nothing here has a panel.
            List<VisualElement> children = new List<VisualElement>(splitView.Children());
            Assert.AreEqual(
                2, children.Count,
                splitName + " must have exactly two children; TwoPaneSplitView does not work otherwise.");
            Assert.AreEqual(
                expectedFixedPaneName, children[splitView.fixedPaneIndex].name,
                splitName + "'s fixed pane is the one the window stores and restores by name.");
        }
    }
}
