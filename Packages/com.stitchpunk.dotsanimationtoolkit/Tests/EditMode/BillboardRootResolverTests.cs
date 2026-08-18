// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Nearest-ancestor inheritance, override precedence, and the depth ordering the runtime relies
    /// on (amendment A44) — asked of real transform hierarchies, without a bake.
    /// </summary>
    public sealed class BillboardRootResolverTests
    {
        private const uint TorsoTargetId = 11u;
        private const uint HandTargetId = 22u;
        private const uint MissingTargetId = 999u;

        private readonly List<Object> createdObjects = new List<Object>();

        private RigAsset rig;
        private Transform actorRoot;
        private Transform torso;
        private Transform hand;
        private Transform itemPivot;

        /// <summary>
        /// Actor / Torso / Hand / ItemPivot — a character with something held in its hand, which is
        /// the case the whole amendment exists for.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            rig = ScriptableObject.CreateInstance<RigAsset>();
            rig.name = "Rig";
            createdObjects.Add(rig);
            rig.layers.Clear();
            rig.layers.Add(new LayerDefinition { displayName = "Base", defaultActive = true });
            rig.targets.Clear();
            rig.targets.Add(new RigTargetDefinition { displayName = "Torso", stableId = TorsoTargetId });
            rig.targets.Add(new RigTargetDefinition { displayName = "Hand", stableId = HandTargetId });

            actorRoot = NewObject("Actor", null);
            torso = NewObject("Torso", actorRoot);
            hand = NewObject("Hand", torso);
            itemPivot = NewObject("ItemPivot", hand);

            AddPart(torso, TorsoTargetId);
            AddPart(hand, HandTargetId);
        }

        [TearDown]
        public void TearDown()
        {
            if (actorRoot != null)
            {
                Object.DestroyImmediate(actorRoot.gameObject);
            }
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                if (createdObjects[objectIndex] != null)
                {
                    Object.DestroyImmediate(createdObjects[objectIndex]);
                }
            }
            createdObjects.Clear();
        }

        private static Transform NewObject(string name, Transform parent)
        {
            GameObject created = new GameObject(name);
            if (parent != null)
            {
                created.transform.SetParent(parent, false);
            }
            return created.transform;
        }

        private static void AddPart(Transform node, uint targetStableId)
        {
            RigTargetAuthoring partAuthoring = node.gameObject.AddComponent<RigTargetAuthoring>();
            partAuthoring.targetStableId = targetStableId;
        }

        private BillboardRootDefinition AddTargetRoot(string displayName, uint targetId, uint rootId)
        {
            BillboardRootDefinition definition = new BillboardRootDefinition
            {
                displayName = displayName,
                stableId = rootId,
                address = new BillboardNodeAddress
                {
                    kind = BillboardAddressKind.RigTarget,
                    targetId = targetId
                }
            };
            rig.billboardRoots.Add(definition);
            return definition;
        }

        private BillboardRootDefinition AddPathRoot(string displayName, string path, uint rootId)
        {
            BillboardRootDefinition definition = new BillboardRootDefinition
            {
                displayName = displayName,
                stableId = rootId,
                address = new BillboardNodeAddress
                {
                    kind = BillboardAddressKind.HierarchyPath,
                    hierarchyPath = path
                }
            };
            rig.billboardRoots.Add(definition);
            return definition;
        }

        // -----------------------------------------------------------------------------------
        // Address resolution.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ATargetAddressFindsThePartCarryingThatStableId()
        {
            AddTargetRoot("Torso", TorsoTargetId, 100u);

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreEqual(1, resolvedRoots.Count);
            Assert.AreSame(torso, resolvedRoots[0].node);
        }

        [Test]
        public void ATargetAddressIgnoresNames_SoAPartMayBeRenamedFreely()
        {
            AddTargetRoot("Torso", TorsoTargetId, 100u);
            torso.gameObject.name = "SomethingElseEntirely";

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreSame(
                torso,
                resolvedRoots[0].node,
                "The stable id is the identity; a rename must never re-point a billboard root.");
        }

        [Test]
        public void APathAddressFindsANodeThatIsNobodysRigTarget()
        {
            AddPathRoot("Held Item", "Torso/Hand/ItemPivot", 200u);

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreSame(
                itemPivot,
                resolvedRoots[0].node,
                "A grouping node with no rig target is exactly why path addressing exists.");
        }

        [Test]
        public void AnEmptyPathAddressesTheActorRoot()
        {
            AddPathRoot("Whole Actor", string.Empty, 300u);

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreSame(actorRoot, resolvedRoots[0].node);
        }

        /// <summary>The half of validation rule V21 that only the prefab can answer.</summary>
        [Test]
        public void AnUnresolvablePathIsReportedRatherThanDroppedSilently()
        {
            AddPathRoot("Nowhere", "Torso/DoesNotExist", 400u);
            List<string> unresolvedRoots = new List<string>();

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, unresolvedRoots);

            Assert.AreEqual(0, resolvedRoots.Count);
            Assert.AreEqual(1, unresolvedRoots.Count);
            StringAssert.Contains("Nowhere", unresolvedRoots[0]);
        }

        [Test]
        public void AnUnresolvableTargetIdIsReportedToo()
        {
            AddTargetRoot("Ghost", MissingTargetId, 500u);
            List<string> unresolvedRoots = new List<string>();

            BillboardRootResolver.Resolve(rig, actorRoot, unresolvedRoots);

            Assert.AreEqual(1, unresolvedRoots.Count);
        }

        // -----------------------------------------------------------------------------------
        // Ordering — the property BillboardResolveSystem depends on.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Depth order is what stops a nested root composing on top of its ancestor's rotation and
        /// turning twice, so it is asserted directly rather than assumed from authoring order.
        /// </summary>
        [Test]
        public void RootsAreReturnedShallowestFirst_WhateverOrderTheyWereAuthoredIn()
        {
            AddPathRoot("Held Item", "Torso/Hand/ItemPivot", 200u);
            AddPathRoot("Whole Actor", string.Empty, 300u);
            AddTargetRoot("Torso", TorsoTargetId, 100u);

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreEqual(3, resolvedRoots.Count);
            Assert.AreSame(actorRoot, resolvedRoots[0].node, "Depth 0 comes first.");
            Assert.AreSame(torso, resolvedRoots[1].node, "Depth 1 next.");
            Assert.AreSame(itemPivot, resolvedRoots[2].node, "Depth 3 last.");
        }

        [Test]
        public void DepthIsMeasuredFromTheActorRoot()
        {
            AddPathRoot("Whole Actor", string.Empty, 300u);
            AddPathRoot("Held Item", "Torso/Hand/ItemPivot", 200u);

            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreEqual(0, resolvedRoots[0].depth);
            Assert.AreEqual(3, resolvedRoots[1].depth);
        }

        // -----------------------------------------------------------------------------------
        // Inheritance and override — the whole point of the amendment.
        // -----------------------------------------------------------------------------------

        [Test]
        public void ANodeInheritsItsNearestAncestorRoot()
        {
            AddPathRoot("Whole Actor", string.Empty, 300u);
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            int handRootIndex =
                BillboardRootResolver.FindNearestRootIndex(resolvedRoots, hand, actorRoot);

            Assert.AreEqual(300u, resolvedRoots[handRootIndex].definition.stableId);
        }

        [Test]
        public void TheNearestAncestorWins_WhenSeveralAreBillboarded()
        {
            AddPathRoot("Whole Actor", string.Empty, 300u);
            AddTargetRoot("Torso", TorsoTargetId, 100u);
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            int handRootIndex =
                BillboardRootResolver.FindNearestRootIndex(resolvedRoots, hand, actorRoot);

            Assert.AreEqual(
                100u,
                resolvedRoots[handRootIndex].definition.stableId,
                "The hand sits under both the actor and the torso; the torso is nearer.");
        }

        /// <summary>
        /// A character billboarding as a whole while the item in its hand billboards independently —
        /// the case A44 was written for.
        /// </summary>
        [Test]
        public void ANodeThatDeclaresItsOwnRootOverridesItsAncestors()
        {
            AddPathRoot("Whole Actor", string.Empty, 300u);
            AddPathRoot("Held Item", "Torso/Hand/ItemPivot", 200u);
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            int pivotRootIndex =
                BillboardRootResolver.FindNearestRootIndex(resolvedRoots, itemPivot, actorRoot);
            int handRootIndex =
                BillboardRootResolver.FindNearestRootIndex(resolvedRoots, hand, actorRoot);

            Assert.AreEqual(
                200u,
                resolvedRoots[pivotRootIndex].definition.stableId,
                "The walk is inclusive of the node itself, which is the override rule.");
            Assert.AreEqual(
                300u,
                resolvedRoots[handRootIndex].definition.stableId,
                "Its parent is unaffected and still billboards with the actor.");
        }

        [Test]
        public void ANodeWithNoBillboardedAncestorBelongsToNoRoot()
        {
            AddPathRoot("Held Item", "Torso/Hand/ItemPivot", 200u);
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreEqual(
                -1,
                BillboardRootResolver.FindNearestRootIndex(resolvedRoots, torso, actorRoot),
                "Billboarding is opt-in; a node above every root transforms normally.");
        }

        [Test]
        public void ARigWithNoBillboardRootsResolvesToNothing()
        {
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, actorRoot, null);

            Assert.AreEqual(0, resolvedRoots.Count);
            Assert.AreEqual(
                -1, BillboardRootResolver.FindNearestRootIndex(resolvedRoots, itemPivot, actorRoot));
        }
    }
}
