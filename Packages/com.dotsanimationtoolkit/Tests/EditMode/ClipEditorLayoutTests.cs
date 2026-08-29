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
            // The top bar is four tabs and nothing else. Exactly one is lit, and SetActiveTab is
            // the only writer of that.
            "tab-clip-editor", "tab-new-rig", "tab-direction-sets", "tab-vat-bake",
            "snap-toggle", "auto-key-toggle",
            "rig-edit-toggle",
            // The floating overlay over the viewport: the shared clip set / rig identity row, and
            // the row of viewport tools beneath it.
            "viewport-overlay", "overlay-identity-row", "overlay-tool-row",
            "clip-set-field", "new-clip-set-button",
            "skinned-source-field", "validation-badge-slot",
            "gizmo-move-toggle", "gizmo-rotate-toggle", "gizmo-scale-toggle",
            "billboard-preview-toggle", "ragdoll-preview-toggle",
            // Transport bar: every control that answers "when", docked above the timeline.
            "transport-bar", "play-toggle", "jump-start-button", "step-back-button",
            "step-forward-button", "jump-end-button",
            "current-frame-field", "current-seconds-field",
            "clip-length-field", "frame-rate-field", "frame-count-label",
            "loop-button", "loop-icon", "playback-speed-field",
            // The captions are the drag handles for the five numbers beside them
            // (MakeCaptionDragHandle). A rename here presents as a number that simply stops
            // scrubbing, with the caption still reading correctly.
            "length-caption", "frame-rate-caption",
            "frame-caption", "seconds-caption", "speed-caption",
            "zoom-slider", "frame-all-button", "frame-selection-button",
            "add-event-button",
            "dock-vertical", "dock-columns", "dock-left", "dock-right",
            "clip-list-pane", "new-clip-button", "delete-clip-button", "clip-list",
            "hierarchy-pane", "hierarchy-empty-label", "hierarchy-tree",
            "edit-prefab-button",
            "viewport-pane", "viewport-status", "viewport-image",
            "inspector-pane", "inspector-content",
            // The status row at the head of the key area: what the next edit will do, plus
            // Quantize Keys, which lives here rather than in the transport bar because it
            // edits keys rather than answering "when".
            "timeline-pane", "timeline-status", "pivot-dropdown", "quantize-keys-button",
            "timeline-scroll", "timeline-row",
            // The strip between the name column and the lanes. A rename presents as a column that
            // simply cannot be dragged, with the cursor still changing over it.
            "track-header-stack", "track-header-resizer", "track-header-column",
            "lane-stack", "lane-column",
            // The VAT bake tab's slot. Nothing is built into it until the tab is first opened, so a
            // rename here would present as a toggle that does nothing rather than as a failure.
            "vat-bake-pane",
            // The New Rig flow's slot, covering the dock the same way the VAT bake tab does
            // (Phase D11). Nothing is built into it until the toggle is first switched on.
            "new-rig-pane",
            // The 2D Direction Sets pane, third of the three cover panes. Same lazily-filled shape,
            // so a rename here is a toggle that lights and shows nothing.
            "direction-sets-pane"
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

        /// <summary>
        /// The four tabs clone as <see cref="ToolbarToggle"/>s, with Clip Editor — and only Clip
        /// Editor — starting lit.
        /// </summary>
        /// <remarks>
        /// Two failures in one test, because they present identically as "the window opened on the
        /// wrong thing". A tab reverted to a <c>ToolbarButton</c> satisfies the generic name check
        /// above while <c>BindTab</c>'s typed <c>Q&lt;ToolbarToggle&gt;</c> returns null and the tab
        /// binds to nothing; and a second tab left at <c>value="true"</c> in the UXML opens the
        /// window with two lit tabs over one pane.
        /// </remarks>
        [Test]
        public void Tabs_CloneAsToolbarToggles_WithOnlyClipEditorLit()
        {
            VisualElement cloneTarget = CloneLayout();

            string[] tabNames = new string[]
            {
                "tab-clip-editor", "tab-new-rig", "tab-direction-sets", "tab-vat-bake"
            };

            List<string> litTabs = new List<string>();
            foreach (string tabName in tabNames)
            {
                ToolbarToggle tab = cloneTarget.Q<ToolbarToggle>(tabName);
                Assert.IsNotNull(
                    tab,
                    tabName + " must clone as a ToolbarToggle, or BindTab's typed Q<> call finds "
                        + "nothing and that tab binds to nothing.");
                if (tab.value)
                {
                    litTabs.Add(tabName);
                }
            }

            CollectionAssert.AreEqual(
                new string[] { "tab-clip-editor" }, litTabs,
                "Exactly one tab starts lit, and it is the Clip Editor — the window opens on the "
                    + "dock, which the other three are drawn over.");
        }

        /// <summary>
        /// The gizmo group clones as three toggles with Move lit, matching
        /// <c>gizmoMode</c>'s own initializer.
        /// </summary>
        /// <remarks>
        /// The field and the UXML are two declarations of one default that no compiler pairs up.
        /// Disagreeing, they open the window with a lit Rotate button over a Move gizmo — which
        /// costs a drag to discover and reads as the gizmo being broken.
        /// </remarks>
        [Test]
        public void GizmoModeToggles_CloneWithMoveLit()
        {
            VisualElement cloneTarget = CloneLayout();

            ToolbarToggle moveToggle = cloneTarget.Q<ToolbarToggle>("gizmo-move-toggle");
            ToolbarToggle rotateToggle = cloneTarget.Q<ToolbarToggle>("gizmo-rotate-toggle");
            ToolbarToggle scaleToggle = cloneTarget.Q<ToolbarToggle>("gizmo-scale-toggle");

            Assert.IsNotNull(moveToggle, "gizmo-move-toggle must clone as a ToolbarToggle.");
            Assert.IsNotNull(rotateToggle, "gizmo-rotate-toggle must clone as a ToolbarToggle.");
            Assert.IsNotNull(scaleToggle, "gizmo-scale-toggle must clone as a ToolbarToggle.");

            Assert.IsTrue(moveToggle.value, "GizmoMode.Move is the window's own default.");
            Assert.IsFalse(rotateToggle.value, "Only one gizmo mode is lit at a time.");
            Assert.IsFalse(scaleToggle.value, "Only one gizmo mode is lit at a time.");
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
