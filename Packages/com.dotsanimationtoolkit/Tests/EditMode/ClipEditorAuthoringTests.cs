// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of the Clip Editor's authoring-side logic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These pin the three places where a mistake is <em>silent and expensive</em>: a drag that
    /// writes to the wrong destination, a hierarchy path that addresses the wrong object, and a
    /// reparent that detaches a branch of a prefab. Each of the three writes to a different asset,
    /// and none of them throws when it gets the answer wrong — the failure shows up later as a
    /// keyframe nobody meant to create, a prefab opened on the wrong node, or a corrupted rig.
    /// </para>
    /// <para>
    /// Deliberately not covered here: anything that needs the window on screen. Whether the Rig Edit
    /// border is visible enough is a judgement a test cannot make, and a fixture asserting that a
    /// USS class is present would pass while the colour was transparent.
    /// </para>
    /// </remarks>
    public sealed class ClipEditorAuthoringTests
    {
        // -----------------------------------------------------------------------------------
        // Gizmo drag routing.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Rig Edit never produces a keyframe, whatever Auto Key is set to.
        /// </summary>
        /// <remarks>
        /// The safety property the mode exists for, and the one worth a fixture of its own. The
        /// viewport states in three places that a drag in Rig Edit edits the prefab; if that can be
        /// falsified by a toggle the user left on from an earlier session, the signage is a lie and
        /// the user finds out when a pose they meant to bake into the rig turns up as a key on one
        /// clip instead.
        /// </remarks>
        [Test]
        public void RigEditMode_NeverKeysAClip_WhateverAutoKeySays()
        {
            foreach (bool autoKey in new bool[] { false, true })
            {
                GizmoDragDestination destination = GizmoDragRouting.Resolve(
                    hasSocketSelected: false,
                    isRigEditMode: true,
                    isAutoKeyEnabled: autoKey,
                    hasPendingEdit: true);

                Assert.AreEqual(
                    GizmoDragDestination.RigBasePose, destination,
                    "Auto Key = " + autoKey + " must not change where a Rig Edit drag goes.");
            }
        }

        /// <summary>
        /// The rest of the routing table, including the two cases that write nothing.
        /// </summary>
        /// <remarks>
        /// Catches a reordering of the rule. A socket has no keyframes at all, so it must win over
        /// both clip modes — resolving it after Auto Key would key a clip on a drag the user made to
        /// place an attachment point.
        /// </remarks>
        [Test]
        public void DragRouting_SendsEachSelectionAndModeToItsOwnDestination()
        {
            Assert.AreEqual(
                GizmoDragDestination.SocketOffset,
                GizmoDragRouting.Resolve(true, false, true, true),
                "A selected socket outranks Auto Key.");

            Assert.AreEqual(
                GizmoDragDestination.SocketOffset,
                GizmoDragRouting.Resolve(true, true, false, true),
                "A selected socket outranks Rig Edit too.");

            Assert.AreEqual(
                GizmoDragDestination.ClipKey,
                GizmoDragRouting.Resolve(false, false, true, true));

            Assert.AreEqual(
                GizmoDragDestination.HeldClipEdit,
                GizmoDragRouting.Resolve(false, false, false, true),
                "With Auto Key off the value is held, not discarded.");

            Assert.AreEqual(
                GizmoDragDestination.Nothing,
                GizmoDragRouting.Resolve(false, false, true, false),
                "A drag that produced no value writes nothing.");
        }

        // -----------------------------------------------------------------------------------
        // Hierarchy paths.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A path round-trips through arbitrary nesting, and a foreign node yields nothing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Paths are how the window addresses an object it cannot hold a reference to: the preview
        /// holds one instance of a prefab and prefab mode opens another. A path that resolved to the
        /// wrong node would reparent the wrong object in a prefab <em>asset</em>, so the failure is
        /// destructive rather than cosmetic.
        /// </para>
        /// <para>
        /// The foreign-node case is the one worth stating: <c>GetHierarchyPath</c> walks up until it
        /// meets the root, and a node from a different hierarchy never does. Returning the partial
        /// path it accumulated on the way would be a path that resolves — somewhere wrong.
        /// </para>
        /// </remarks>
        [Test]
        public void HierarchyPath_RoundTripsThroughNesting_AndRefusesAForeignNode()
        {
            GameObject root = new GameObject("Root");
            GameObject torso = new GameObject("Torso");
            GameObject neck = new GameObject("Neck");
            GameObject head = new GameObject("Head");
            GameObject stranger = new GameObject("Torso");
            try
            {
                torso.transform.SetParent(root.transform, false);
                neck.transform.SetParent(torso.transform, false);
                head.transform.SetParent(neck.transform, false);

                string path = PrefabAuthoringBridge.GetHierarchyPath(
                    head.transform, root.transform);
                Assert.AreEqual("Torso/Neck/Head", path);

                Assert.AreSame(
                    head.transform,
                    PrefabAuthoringBridge.ResolveByPath(root.transform, path),
                    "The path must resolve back to the node it was built from.");

                Assert.AreEqual(
                    string.Empty,
                    PrefabAuthoringBridge.GetHierarchyPath(root.transform, root.transform),
                    "The root's own path is empty, and empty resolves to the root.");
                Assert.AreSame(
                    root.transform,
                    PrefabAuthoringBridge.ResolveByPath(root.transform, string.Empty));

                // A name that exists at the wrong depth must not be found by a walk that skips a
                // level: "Neck" is under Torso, not under the root.
                Assert.IsNull(PrefabAuthoringBridge.ResolveByPath(root.transform, "Neck"));

                Assert.AreEqual(
                    string.Empty,
                    PrefabAuthoringBridge.GetHierarchyPath(stranger.transform, root.transform),
                    "A node outside the root has no path under it — not even a partial one.");
            }
            finally
            {
                Object.DestroyImmediate(stranger);
                Object.DestroyImmediate(root);
            }
        }

        // -----------------------------------------------------------------------------------
        // Reparent guards.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// The reparents that would detach a branch are refused before anything is written.
        /// </summary>
        /// <remarks>
        /// Parenting a node under itself or under one of its own descendants makes a cycle and takes
        /// the whole subtree out of the prefab. Unity reports that after the fact; refusing here
        /// means the asset is never opened, let alone saved. The legal case is asserted alongside so
        /// that a guard which simply rejected everything would fail this fixture too.
        /// </remarks>
        [Test]
        public void Reparent_RefusesCyclesAndNoOps_ButAllowsARealMove()
        {
            GameObject root = new GameObject("Root");
            GameObject arm = new GameObject("Arm");
            GameObject hand = new GameObject("Hand");
            GameObject leg = new GameObject("Leg");
            try
            {
                arm.transform.SetParent(root.transform, false);
                hand.transform.SetParent(arm.transform, false);
                leg.transform.SetParent(root.transform, false);

                string error;

                Assert.IsFalse(
                    RigStructureEditor.ValidateReparent(arm.transform, arm.transform, out error),
                    "An object cannot be parented under itself.");
                Assert.IsNotEmpty(error, "A refusal must say why.");

                Assert.IsFalse(
                    RigStructureEditor.ValidateReparent(arm.transform, hand.transform, out error),
                    "Parenting a branch under its own descendant would detach it.");
                Assert.IsNotEmpty(error);

                Assert.IsFalse(
                    RigStructureEditor.ValidateReparent(hand.transform, arm.transform, out error),
                    "Already a child of that parent: nothing to do.");

                Assert.IsFalse(
                    RigStructureEditor.ValidateReparent(null, arm.transform, out error),
                    "A node that has gone cannot be moved.");

                Assert.IsTrue(
                    RigStructureEditor.ValidateReparent(hand.transform, leg.transform, out error),
                    "Moving a hand onto a leg is legal, however odd.");
                Assert.IsEmpty(error, "A success reports no error.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
