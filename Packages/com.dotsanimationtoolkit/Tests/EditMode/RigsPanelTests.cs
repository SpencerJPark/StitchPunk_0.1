// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="RigsPanel"/>'s rig selection and target tick sync.</summary>
    public sealed class RigsPanelTests
    {
        private GameObject sourcePrefabRoot;
        private GameObject torsoNode;
        private GameObject headNode;
        private RigAsset rigAsset;
        private RigAsset secondRigAsset;

        [SetUp]
        public void SetUp()
        {
            sourcePrefabRoot = new GameObject("SourceRoot");
            sourcePrefabRoot.AddComponent<MeshRenderer>();

            torsoNode = new GameObject("Torso");
            torsoNode.transform.SetParent(sourcePrefabRoot.transform);
            torsoNode.AddComponent<MeshRenderer>();

            headNode = new GameObject("Head");
            headNode.transform.SetParent(sourcePrefabRoot.transform);
            headNode.AddComponent<MeshRenderer>();

            rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            rigAsset.sourcePrefab = sourcePrefabRoot;
            rigAsset.targets.Add(new RigTargetDefinition
            {
                displayName = "Torso",
                sourceNodePath = "Torso",
            });
            rigAsset.EnsureStableIds();

            secondRigAsset = ScriptableObject.CreateInstance<RigAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(rigAsset);
            Object.DestroyImmediate(secondRigAsset);
            Object.DestroyImmediate(sourcePrefabRoot);
        }

        // Hiding the tab drops the split pair's stored dimension back to "uninitialised". The outer
        // split's fixed pane is the inner split, so without a floor on that element the tab comes
        // back as nothing but the preview — the columns' own minWidths sit inside it and cannot help.
        [Test]
        public void InnerSplitView_CarriesItsOwnWidthFloor_SoAHiddenTabCannotComeBackCollapsed()
        {
            RigsPanel panel = new RigsPanel();
            try
            {
                List<CoverPaneSplitView> splitViews = panel.Query<CoverPaneSplitView>().ToList();
                Assert.AreEqual(2, splitViews.Count, "The tab is built from an outer and an inner remembered split view.");

                CoverPaneSplitView innerSplitView = splitViews[1];
                Assert.AreEqual(
                    560f,
                    innerSplitView.style.minWidth.value.value,
                    "The inner split must floor at the sum of the two column minimums (200 + 360).");
            }
            finally
            {
                panel.Dispose();
            }
        }

        [Test]
        public void SelectRig_ShowsTheRigsTargetsTicked()
        {
            RigsPanel panel = new RigsPanel();
            try
            {
                panel.SelectRig(rigAsset);

                Assert.AreEqual(rigAsset, panel.SelectedRig);

                VisualElement targetsColumn = panel.Q<VisualElement>("rig-targets-column");
                List<Toggle> candidateToggles = targetsColumn.Query<Toggle>().ToList()
                    .Where(toggle => toggle.parent != null
                        && toggle.parent.ClassListContains("toolkit-list-row"))
                    .ToList();

                Assert.AreEqual(2, candidateToggles.Count);

                List<Toggle> tickedToggles = candidateToggles.Where(toggle => toggle.value).ToList();
                Assert.AreEqual(1, tickedToggles.Count);
                Assert.AreEqual("Torso", tickedToggles[0].tooltip);
            }
            finally
            {
                panel.Dispose();
            }
        }

        [Test]
        public void SelectRig_WritesTheSharedSelection_AndFollowsIt()
        {
            ActiveAssetSelection selection = new ActiveAssetSelection();
            RigsPanel panel = new RigsPanel();
            try
            {
                panel.Bind(selection);

                panel.SelectRig(rigAsset);
                Assert.AreEqual(rigAsset, selection.Rig);

                selection.SetRig(secondRigAsset);
                Assert.AreEqual(secondRigAsset, panel.SelectedRig);
            }
            finally
            {
                panel.Dispose();
            }
        }

        [Test]
        public void FocusingEachTargetRow_CarriesThatTargetsKindIntoTheTargetCard()
        {
            rigAsset.targets[0].kind = TargetKind.Quad;
            rigAsset.targets.Add(new RigTargetDefinition
            {
                displayName = "Head",
                sourceNodePath = "Head",
                kind = TargetKind.VatMesh,
            });
            rigAsset.EnsureStableIds();

            RigsPanel panel = new RigsPanel();
            try
            {
                panel.SelectRig(rigAsset);

                VisualElement targetsColumn = panel.Q<VisualElement>("rig-targets-column");
                Button kindButton = targetsColumn.Q<Button>("rig-target-kind-button");

                // The subject moved with SG-D6, so this asserts the new one. Kind and Tag used to
                // sit on every row -- a 330px row could not hold a node name and two chips -- and
                // now live in one Target card mirroring the focused row, so the kinds are read one
                // at a time rather than side by side. What is still guarded is the fact that
                // mattered: each target's kind reaches the UI from the rig.
                panel.FocusTargetRow(0);
                Assert.AreEqual("Kind: Quad", kindButton.text);

                panel.FocusTargetRow(1);
                Assert.AreEqual("Kind: VAT Mesh", kindButton.text);
            }
            finally
            {
                panel.Dispose();
            }
        }

        [Test]
        public void SelectRig_WithNoSourcePrefab_ShowsTheAssignPrefabHint_AndNoRows()
        {
            RigAsset rigWithoutPrefab = ScriptableObject.CreateInstance<RigAsset>();
            RigsPanel panel = new RigsPanel();
            try
            {
                panel.SelectRig(rigWithoutPrefab);

                VisualElement targetsColumn = panel.Q<VisualElement>("rig-targets-column");
                List<Toggle> candidateToggles = targetsColumn.Query<Toggle>().ToList()
                    .Where(toggle => toggle.parent != null
                        && toggle.parent.ClassListContains("toolkit-list-row"))
                    .ToList();

                Assert.AreEqual(0, candidateToggles.Count);

                Label summaryLabel = targetsColumn.Query<Label>().ToList()
                    .First(label => label.text.Contains("Assign a source prefab"));
                Assert.IsTrue(summaryLabel.text.Contains("Assign a source prefab"));
            }
            finally
            {
                panel.Dispose();
                Object.DestroyImmediate(rigWithoutPrefab);
            }
        }
    }
}
