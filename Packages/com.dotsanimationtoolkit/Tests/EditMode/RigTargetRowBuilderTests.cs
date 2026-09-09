// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="RigTargetRowBuilder"/>'s new-rig scan and existing-rig merge.</summary>
    public sealed class RigTargetRowBuilderTests
    {
        private GameObject hierarchyRoot;
        private RigAsset rigAsset;

        [TearDown]
        public void TearDown()
        {
            if (hierarchyRoot != null)
            {
                Object.DestroyImmediate(hierarchyRoot);
            }

            if (rigAsset != null)
            {
                Object.DestroyImmediate(rigAsset);
            }
        }

        [Test]
        public void BuildForNewRig_SkipsTheRootRenderer_AndPreTicksOnlyEnabledActiveNodes()
        {
            hierarchyRoot = new GameObject("Root");
            hierarchyRoot.AddComponent<MeshRenderer>();

            GameObject enabledChild = new GameObject("EnabledChild");
            enabledChild.AddComponent<MeshRenderer>();
            enabledChild.transform.SetParent(hierarchyRoot.transform, false);

            GameObject disabledChild = new GameObject("DisabledChild");
            MeshRenderer disabledRenderer = disabledChild.AddComponent<MeshRenderer>();
            disabledRenderer.enabled = false;
            disabledChild.transform.SetParent(hierarchyRoot.transform, false);

            List<RigTargetRow> rows = RigTargetRowBuilder.BuildForNewRig(hierarchyRoot);

            Assert.AreEqual(2, rows.Count);

            RigTargetRow enabledRow = rows.Find(row => row.DisplayName == "EnabledChild");
            RigTargetRow disabledRow = rows.Find(row => row.DisplayName == "DisabledChild");

            Assert.IsNotNull(enabledRow);
            Assert.IsNotNull(disabledRow);
            Assert.IsTrue(enabledRow.PreTicked);
            Assert.IsFalse(disabledRow.PreTicked);
        }

        [Test]
        public void BuildForRig_TicksExistingTargets_AndTrailsMissingOnesLast()
        {
            hierarchyRoot = new GameObject("Root");

            GameObject boundChild = new GameObject("Child");
            boundChild.AddComponent<MeshRenderer>();
            boundChild.transform.SetParent(hierarchyRoot.transform, false);

            GameObject unboundChild = new GameObject("OtherChild");
            unboundChild.AddComponent<MeshRenderer>();
            unboundChild.transform.SetParent(hierarchyRoot.transform, false);

            rigAsset = ScriptableObject.CreateInstance<RigAsset>();
            rigAsset.sourcePrefab = hierarchyRoot;

            RigTargetDefinition boundTarget = new RigTargetDefinition
            {
                sourceNodePath = "Child",
                displayName = "ChildTarget",
                tagId = 5u,
            };
            RigTargetDefinition missingTarget = new RigTargetDefinition
            {
                sourceNodePath = "Ghost",
                displayName = "GhostTarget",
                tagId = 7u,
            };
            rigAsset.targets.Add(boundTarget);
            rigAsset.targets.Add(missingTarget);
            rigAsset.EnsureStableIds();

            List<RigTargetRow> rows = RigTargetRowBuilder.BuildForRig(rigAsset);

            Assert.AreEqual(3, rows.Count);

            RigTargetRow boundRow = rows.Find(row => row.SourceNodePath == "Child");
            RigTargetRow unboundRow = rows.Find(row => row.SourceNodePath == "OtherChild");
            RigTargetRow lastRow = rows[rows.Count - 1];

            Assert.IsNotNull(boundRow);
            Assert.IsTrue(boundRow.IsTarget);
            Assert.AreEqual(5u, boundRow.TagId);
            Assert.AreEqual(boundTarget.Id.Value, boundRow.TargetStableId);
            Assert.AreNotEqual(0u, boundRow.TargetStableId);

            Assert.IsNotNull(unboundRow);
            Assert.IsFalse(unboundRow.IsTarget);

            Assert.IsTrue(lastRow.IsMissingNode);
            Assert.IsTrue(lastRow.IsTarget);
            Assert.AreEqual(missingTarget.Id.Value, lastRow.TargetStableId);
        }
    }
}
