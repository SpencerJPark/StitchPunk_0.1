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
    /// <summary>EditMode coverage of <see cref="RigsPanel"/>'s Edit/Create mode switch and target tick sync.</summary>
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

        [Test]
        public void SelectRig_EntersEditMode_WithTheRigsTargetsTicked()
        {
            RigsPanel panel = new RigsPanel();
            try
            {
                panel.SelectRig(rigAsset);

                Assert.AreEqual(RigsPanel.EditorMode.Edit, panel.Mode);
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

                panel.BeginCreate();

                Assert.AreEqual(RigsPanel.EditorMode.Create, panel.Mode);
                Assert.IsNull(panel.SelectedRig);
            }
            finally
            {
                panel.Dispose();
            }
        }
    }
}
