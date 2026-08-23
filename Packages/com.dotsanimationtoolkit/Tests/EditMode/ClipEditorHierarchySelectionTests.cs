// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <c>ClipEditorWindow.ApplyHierarchySelection</c>'s
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
    /// targetId" would have been equally wrong in exactly the way the bug was.
    /// </para>
    /// </remarks>
    public sealed class ClipEditorHierarchySelectionTests
    {
        private const int ActiveItemId = 3;

        [Test]
        public void ApplyHierarchySelection_ClaimedPrefabTransform_SetsSelectedTargetId()
        {
            ClipEditorWindow window = ScriptableObject.CreateInstance<ClipEditorWindow>();
            try
            {
                SelectSingleHierarchyItem(window, "PrefabTransform", targetId: 7u, displayName: "Torso");
                InvokeApplyHierarchySelection(window);

                Assert.AreEqual(
                    7u, ReadSelectedTargetId(window),
                    "A claimed part (PrefabTransform row, non-zero targetId) must key the gizmo, "
                        + "same as a declared RigTarget row does — this is the exact D12 regression: "
                        + "the common case for an ordinary clip-authoring drag was forced to 0.");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(window);
            }
        }

        [Test]
        public void ApplyHierarchySelection_RigTargetRow_StillSetsSelectedTargetId()
        {
            ClipEditorWindow window = ScriptableObject.CreateInstance<ClipEditorWindow>();
            try
            {
                SelectSingleHierarchyItem(window, "RigTarget", targetId: 9u, displayName: "Head");
                InvokeApplyHierarchySelection(window);

                Assert.AreEqual(
                    9u, ReadSelectedTargetId(window),
                    "The pre-existing RigTarget case must keep working unchanged.");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(window);
            }
        }

        [Test]
        public void ApplyHierarchySelection_UnclaimedPrefabTransform_LeavesSelectedTargetIdZero()
        {
            ClipEditorWindow window = ScriptableObject.CreateInstance<ClipEditorWindow>();
            try
            {
                SelectSingleHierarchyItem(window, "PrefabTransform", targetId: 0u, displayName: "Bone");
                InvokeApplyHierarchySelection(window);

                Assert.AreEqual(
                    0u, ReadSelectedTargetId(window),
                    "A bare grouping transform or skinned bone with no claimed part has nothing for "
                        + "a TransformTrack to key against — this must still show no clip-authoring "
                        + "gizmo (Rig Edit's separate, node-only gate is unaffected).");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(window);
            }
        }

        /// <summary>
        /// Builds one <c>HierarchyItem</c> of the given private nested <c>HierarchyItemKind</c>,
        /// selects it, and marks it active — the state <c>ApplyHierarchySelection</c> reads.
        /// </summary>
        private static void SelectSingleHierarchyItem(
            ClipEditorWindow window, string kindName, uint targetId, string displayName)
        {
            Type windowType = typeof(ClipEditorWindow);
            Type kindType = windowType.GetNestedType("HierarchyItemKind", BindingFlags.NonPublic);
            Type itemType = windowType.GetNestedType("HierarchyItem", BindingFlags.NonPublic);
            object item = Activator.CreateInstance(itemType, nonPublic: true);

            // HierarchyItem itself is a private nested class, but its fields are public — the
            // class's accessibility, not the fields', so these two lookups need different flags.
            itemType.GetField("kind", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, Enum.Parse(kindType, kindName));
            itemType.GetField("displayName", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, displayName);
            itemType.GetField("targetId", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, targetId);
            itemType.GetField("previewIndex", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(item, 0);

            FieldInfo hierarchyItemsByIdField = windowType.GetField(
                "hierarchyItemsById", BindingFlags.NonPublic | BindingFlags.Instance);
            IDictionary hierarchyItemsById = (IDictionary)hierarchyItemsByIdField.GetValue(window);
            hierarchyItemsById.Add(ActiveItemId, item);

            FieldInfo selectedHierarchyItemsField = windowType.GetField(
                "selectedHierarchyItems", BindingFlags.NonPublic | BindingFlags.Instance);
            IList selectedHierarchyItems = (IList)selectedHierarchyItemsField.GetValue(window);
            selectedHierarchyItems.Add(item);

            windowType.GetField("activeHierarchyItemId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(window, ActiveItemId);
        }

        private static void InvokeApplyHierarchySelection(ClipEditorWindow window)
        {
            typeof(ClipEditorWindow)
                .GetMethod("ApplyHierarchySelection", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(window, null);
        }

        private static uint ReadSelectedTargetId(ClipEditorWindow window)
        {
            return (uint)typeof(ClipEditorWindow)
                .GetField("selectedTargetId", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(window);
        }
    }
}
