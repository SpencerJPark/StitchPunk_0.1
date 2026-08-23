// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
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
            "snap-toggle", "auto-key-toggle",
            "rig-edit-toggle", "billboard-preview-toggle",
            "ragdoll-preview-toggle", "vat-bake-toggle",
            "skinned-source-field", "validation-badge-slot",
            // Transport bar: every control that answers "when", docked above the timeline.
            "transport-bar", "play-toggle", "jump-start-button", "step-back-button",
            "step-forward-button", "jump-end-button",
            "current-frame-field", "current-seconds-field",
            "clip-length-field", "frame-rate-field", "frame-count-label",
            "loop-button", "loop-icon", "playback-speed-field",
            "zoom-slider", "frame-all-button", "frame-selection-button",
            "dock-vertical", "dock-columns", "dock-left", "dock-right",
            "clip-list-pane", "new-clip-button", "delete-clip-button", "clip-list",
            "hierarchy-pane", "hierarchy-empty-label", "hierarchy-tree",
            "edit-prefab-button",
            "viewport-pane", "viewport-status", "viewport-image",
            // Where the validation findings hang. Empty in the UXML and filled by the badge, so a
            // rename here presents as an error button that appears to do nothing.
            "validation-overlay-slot",
            "inspector-pane", "inspector-content",
            // The status row at the head of the key area: what the next edit will do, plus
            // Quantize Keys, which lives here rather than in the transport bar because it
            // edits keys rather than answering "when".
            "timeline-pane", "timeline-status", "pivot-dropdown", "quantize-keys-button",
            "timeline-scroll", "timeline-row",
            "track-header-stack", "track-header-column", "lane-stack", "lane-column",
            // The VAT bake tab's slot. Nothing is built into it until the tab is first opened, so a
            // rename here would present as a toggle that does nothing rather than as a failure.
            "vat-bake-pane"
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

        /// <summary>
        /// The ragdoll toggle clones as a real <see cref="ToolbarToggle"/>, defaulting off (Phase
        /// D6, spec §8.4).
        /// </summary>
        /// <remarks>
        /// <c>ClipEditorWindow.BindToolbar</c> resolves this element through
        /// <c>Q&lt;ToolbarToggle&gt;("ragdoll-preview-toggle")</c> and calls
        /// <c>RegisterValueChangedCallback</c> on the result: a rename to a plain <c>Toggle</c> or
        /// to a <c>Button</c> would satisfy <see cref="ClonedLayout_ContainsEverySlotTheWindowResolves"/>'s
        /// generic <c>Q&lt;VisualElement&gt;</c> lookup while making the typed query return null and
        /// the callback bind to nothing — precisely the "toggle that visibly exists but does
        /// nothing" failure the placeholder this phase replaces used to warn about in its own
        /// tooltip. Whether the callback itself fires is not checkable without the window on
        /// screen (this file's own scope, stated above); this is the static half of "bound and has
        /// a callback" that can be.
        /// </remarks>
        [Test]
        public void RagdollPreviewToggle_ClonesAsAToolbarToggle_DefaultingOff()
        {
            VisualElement cloneTarget = CloneLayout();
            ToolbarToggle ragdollToggle = cloneTarget.Q<ToolbarToggle>("ragdoll-preview-toggle");
            Assert.IsNotNull(
                ragdollToggle,
                "ragdoll-preview-toggle must clone as a ToolbarToggle, or BindToolbar's typed Q<> "
                    + "call finds nothing and the toggle's callback never binds.");
            Assert.IsFalse(
                ragdollToggle.value,
                "A ragdoll must start off — spec §8.4 has no 'preview opens already dropped' case.");
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
