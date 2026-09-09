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
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(rigAsset);
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
                List<TwoPaneSplitView> splitViews = panel.Query<TwoPaneSplitView>().ToList();
                Assert.AreEqual(2, splitViews.Count, "The tab is built from an outer and an inner split view.");

                TwoPaneSplitView innerSplitView = splitViews[1];
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
                        && toggle.parent.ClassListContains("toolkit-box__header"))
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
                        && toggle.parent.ClassListContains("toolkit-box__header"))
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
