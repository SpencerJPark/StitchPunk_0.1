// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <c>RigHierarchyPane.ApplyHierarchySelection</c>'s
    /// <c>selectedTargetId</c> assignment (Phase D12, Task 1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The defect:</strong> <c>selectedTargetId</c> — the field
    /// <c>RefreshGizmo</c>/<c>TryBeginGizmoDrag</c> key the viewport gizmo off of — was only ever set
    /// for a <c>RigTarget</c> row (the rare "unclaimed target, no node" fallback). A claimed part
    /// with a real prefab node (<c>PrefabTransform</c> kind, non-zero <c>targetId</c>) — the common
    /// case for ordinary clip-authoring drags — had it forced to zero even though the row named a
    /// real target. <c>GizmoDragRouting.ShouldShowTransformGizmo</c> then read that zero as "nothing
    /// selected" and hid the gizmo.
    /// </para>
    /// <para>
    /// This exercises the real private method through reflection rather than re-deriving the rule in
    /// a standalone helper, because the rule now has no branch left to state on its own — the fix is
    /// that a claimed part's <c>targetId</c> flows through unconditionally. Asserting on the actual
    /// method is what would have caught the regression: a hand-written mirror of "just returns
    /// targetId" would have been equally wrong in exactly the way the bug was. The method moved from
    /// the window to the hierarchy pane with the rest of the tree code; the pane is bound to no
    /// visual tree here, only to a fresh selection and session, which is all the method reads.
    /// </para>
    /// </remarks>
    public sealed class ClipEditorHierarchySelectionTests
    {
        private const int ActiveItemId = 3;

        [Test]
        public void ApplyHierarchySelection_ClaimedPrefabTransform_SetsSelectedTargetId()
        {
            RigHierarchyPane pane = BindUnparentedPane();
            SelectSingleHierarchyItem(pane, "PrefabTransform", targetId: 7u, displayName: "Torso");
            InvokeApplyHierarchySelection(pane);

            Assert.AreEqual(
                7u, ReadSelectedTargetId(pane),
                "A claimed part (PrefabTransform row, non-zero targetId) must key the gizmo, "
                    + "same as a declared RigTarget row does — this is the exact D12 regression: "
                    + "the common case for an ordinary clip-authoring drag was forced to 0.");
        }

        [Test]
        public void ApplyHierarchySelection_RigTargetRow_StillSetsSelectedTargetId()
        {
            RigHierarchyPane pane = BindUnparentedPane();
            SelectSingleHierarchyItem(pane, "RigTarget", targetId: 9u, displayName: "Head");
            InvokeApplyHierarchySelection(pane);

            Assert.AreEqual(
                9u, ReadSelectedTargetId(pane),
                "The pre-existing RigTarget case must keep working unchanged.");
        }

        [Test]
        public void ApplyHierarchySelection_UnclaimedPrefabTransform_LeavesSelectedTargetIdZero()
        {
            RigHierarchyPane pane = BindUnparentedPane();
            SelectSingleHierarchyItem(pane, "PrefabTransform", targetId: 0u, displayName: "Bone");
            InvokeApplyHierarchySelection(pane);

            Assert.AreEqual(
                0u, ReadSelectedTargetId(pane),
                "A bare grouping transform or skinned bone with no claimed part has nothing for "
                    + "a TransformTrack to key against — this must still show no clip-authoring "
                    + "gizmo (Rig Edit's separate, node-only gate is unaffected).");
        }

        // No pane root and no preview: ApplyHierarchySelection guards both, and the session it
        // publishes into is what the window would otherwise have handed it.
        private static RigHierarchyPane BindUnparentedPane()
        {
            RigHierarchyPane pane = new RigHierarchyPane();
            pane.Bind(null, new ActiveAssetSelection(), new ClipEditorSession(), null);
            return pane;
        }

        /// <summary>
        /// Builds one <c>HierarchyItem</c> of the given internal <c>HierarchyItemKind</c>,
        /// selects it, and marks it active — the state <c>ApplyHierarchySelection</c> reads.
        /// </summary>
        private static void SelectSingleHierarchyItem(
            RigHierarchyPane pane, string kindName, uint targetId, string displayName)
        {
            Type paneType = typeof(RigHierarchyPane);
            Type kindType = paneType.Assembly.GetType("DotsAnimationToolkit.Editor.HierarchyItemKind");
            Type itemType = paneType.Assembly.GetType("DotsAnimationToolkit.Editor.HierarchyItem");
            Assert.IsNotNull(kindType, "HierarchyItemKind must be a file-scope type beside RigHierarchyPane.");
            Assert.IsNotNull(itemType, "HierarchyItem must be a file-scope type beside RigHierarchyPane.");
            object item = Activator.CreateInstance(itemType, nonPublic: true);

            // HierarchyItem itself is internal, but its fields are public — the class's
            // accessibility, not the fields', so these two lookups need different flags.
            itemType.GetField("kind", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, Enum.Parse(kindType, kindName));
            itemType.GetField("displayName", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, displayName);
            itemType.GetField("targetId", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, targetId);
            itemType.GetField("previewIndex", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, 0);

            FieldInfo hierarchyItemsByIdField = paneType.GetField(
                "hierarchyItemsById", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary hierarchyItemsById = (IDictionary)hierarchyItemsByIdField.GetValue(pane);
            hierarchyItemsById.Add(ActiveItemId, item);

            FieldInfo selectedHierarchyItemsField = paneType.GetField(
                "selectedHierarchyItems", BindingFlags.NonPublic | BindingFlags.Instance);
            IList selectedHierarchyItems = (IList)selectedHierarchyItemsField.GetValue(pane);
            selectedHierarchyItems.Add(item);

            paneType.GetField("activeHierarchyItemId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(pane, ActiveItemId);
        }

        private static void InvokeApplyHierarchySelection(RigHierarchyPane pane)
        {
            typeof(RigHierarchyPane)
                .GetMethod("ApplyHierarchySelection", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(pane, null);
        }

        private static uint ReadSelectedTargetId(RigHierarchyPane pane)
        {
            return (uint)typeof(RigHierarchyPane)
                .GetField("selectedTargetId", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(pane);
        }
    }
}
