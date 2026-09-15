// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>The Ragdoll tab's header summary: how many bodies a rig declares, and how many of
    /// them are joints — a body with another body above it in the hierarchy.</summary>
    public sealed class RagdollBodySummaryResolverTests
    {
        private const string RootNodePath = "Root";
        private const string ArmNodePath = "Root/Arm";
        private const string HandNodePath = "Root/Arm/Hand";
        private const string OtherRootNodePath = "RootOther";

        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                if (createdObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(createdObjects[objectIndex]);
                }
            }
            createdObjects.Clear();
        }

        [Test]
        public void Summary_CountsJointsAsBodiesWithAParent()
        {
            RigAsset rig = BuildRigWithBodies(RootNodePath, ArmNodePath, HandNodePath);

            RagdollBodySummary summary = RagdollBodySummaryResolver.Resolve(rig);

            Assert.AreEqual(3, summary.bodyCount, "Every non-null body row counts.");
            Assert.AreEqual(2, summary.jointCount, "Only the root body has no body above it.");
            Assert.AreEqual("3 bodies · 2 joints", summary.text);
        }

        [Test]
        public void Summary_CountsNoJointsWhenEveryBodyIsARoot()
        {
            RigAsset rig = BuildRigWithBodies(RootNodePath, OtherRootNodePath);

            RagdollBodySummary summary = RagdollBodySummaryResolver.Resolve(rig);

            Assert.AreEqual(2, summary.bodyCount);
            Assert.AreEqual(
                0,
                summary.jointCount,
                "A shared name prefix is not ancestry: only a path break at a separator is.");
        }

        [Test]
        public void Summary_FlagsABodyWhoseTargetNoLongerResolves()
        {
            RigAsset rig = BuildRigWithBodies(RootNodePath, ArmNodePath);
            rig.ragdollBodies[1].address.targetId = 987654u;

            RagdollBodySummary summary = RagdollBodySummaryResolver.Resolve(rig);

            Assert.AreEqual(2, summary.bodyCount);
            Assert.AreEqual(1, summary.unresolvedCount);
            Assert.IsFalse(RagdollBodySummaryResolver.IsBodyResolved(rig, rig.ragdollBodies[1]));
            Assert.IsTrue(RagdollBodySummaryResolver.IsBodyResolved(rig, rig.ragdollBodies[0]));
        }

        // One rig target per node path, one ragdoll body welded to each: the parent chain the
        // resolver reports has to come from the paths alone, since a fixture has no prefab to walk.
        private RigAsset BuildRigWithBodies(params string[] nodePaths)
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            createdObjects.Add(rig);

            rig.targets = new List<RigTargetDefinition>();
            for (int pathIndex = 0; pathIndex < nodePaths.Length; pathIndex++)
            {
                RigTargetDefinition target = new RigTargetDefinition
                {
                    displayName = nodePaths[pathIndex],
                    sourceNodePath = nodePaths[pathIndex]
                };
                rig.targets.Add(target);
            }
            rig.EnsureStableIds();

            rig.ragdollBodies = new List<RagdollBodyDefinition>();
            for (int pathIndex = 0; pathIndex < nodePaths.Length; pathIndex++)
            {
                RagdollBodyDefinition body = new RagdollBodyDefinition
                {
                    displayName = nodePaths[pathIndex],
                    address = new RigNodeAddress
                    {
                        kind = RigNodeAddressKind.RigTarget,
                        targetId = rig.targets[pathIndex].Id.Value
                    }
                };
                rig.ragdollBodies.Add(body);
            }
            rig.EnsureStableIds();

            return rig;
        }
    }
}
