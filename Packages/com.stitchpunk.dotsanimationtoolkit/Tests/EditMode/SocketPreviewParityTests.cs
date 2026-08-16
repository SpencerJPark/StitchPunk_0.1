// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using StitchPunk.AnimationToolkit.Editor;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of the claim that a socket marker sits where the runtime will put it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The preview's entire value is that what you tune is what plays. A socket marker composes the
    /// followed part's local pose with the socket's offset, and so does
    /// <c>SocketResolveSystem</c>; if the two ever disagree, an author tunes an offset against a
    /// marker and ships an attachment that appears somewhere else.
    /// </para>
    /// <para>
    /// The rotated case is the one that matters. An offset applied without rotating it into the
    /// followed thing's space is <em>correct while the part is unrotated</em>, which is exactly the
    /// state a rig is usually authored in — so the mistake survives casual checking and shows up
    /// only once something turns.
    /// </para>
    /// </remarks>
    public sealed class SocketPreviewParityTests
    {
        private const float Tolerance = 1e-4f;

        private readonly List<Object> created = new List<Object>();
        private PreviewRigMirror rigMirror;
        private PreviewSocketMarkers socketMarkers;

        [TearDown]
        public void DisposeFixture()
        {
            if (socketMarkers != null)
            {
                socketMarkers.Dispose();
                socketMarkers = null;
            }
            if (rigMirror != null)
            {
                rigMirror.Dispose();
                rigMirror = null;
            }
            for (int index = 0; index < created.Count; index++)
            {
                if (created[index] != null)
                {
                    Object.DestroyImmediate(created[index]);
                }
            }
            created.Clear();
        }

        private RigAsset BuildRig(uint targetId, Vector3 socketOffset, Vector3 socketEuler)
        {
            RigAsset rig = ScriptableObject.CreateInstance<RigAsset>();
            created.Add(rig);

            rig.targets = new List<RigTargetDefinition>
            {
                new RigTargetDefinition { displayName = "Hand", stableId = targetId }
            };
            rig.sockets = new List<SocketDefinition>
            {
                new SocketDefinition
                {
                    displayName = "Grip",
                    stableId = 77u,
                    mode = SocketAttachMode.RigTarget,
                    targetId = targetId,
                    localPosition = socketOffset,
                    localEulerAngles = socketEuler
                }
            };
            return rig;
        }

        /// <summary>
        /// The marker is the followed part's pose with the offset rotated into its space.
        /// </summary>
        /// <remarks>
        /// Catches the offset being added in the wrong space — the mistake that is invisible until
        /// the part rotates. With the hand turned 90° about Z, an offset of +0.2 along X must come
        /// out along +Y; adding it unrotated would leave it along X and put the attachment on the
        /// wrong side of the hand.
        /// </remarks>
        [Test]
        public void SocketMarker_ComposesTheOffsetInTheFollowedPartsSpace()
        {
            const uint handId = 4242u;
            Vector3 offset = new Vector3(0.2f, 0f, 0f);
            RigAsset rig = BuildRig(handId, offset, Vector3.zero);

            rigMirror = new PreviewRigMirror();
            rigMirror.Rebuild(rig);

            socketMarkers = new PreviewSocketMarkers();
            socketMarkers.Rebuild(rig, null);
            Assert.AreEqual(1, socketMarkers.MarkerCount, "The rig declares one socket.");

            // Pose the hand: a metre up, turned a quarter turn about Z. Rotation crosses the API in
            // radians, which is the runtime's unit and what ApplyPose expects.
            TargetPose pose = new TargetPose
            {
                localPosition = new float3(0f, 1f, 0f),
                rotation = new float3(0f, 0f, math.radians(90f)),
                scale = new float3(1f, 1f, 1f),
                sliceIndex = 0,
                atlasRect = ClipSampler.IdentityAtlasRect
            };
            rigMirror.ApplyPose(handId, in pose);
            socketMarkers.UpdateMarkers(rigMirror, null);

            Transform marker = socketMarkers.GetMarker(77u);
            Assert.IsNotNull(marker);

            // Rotated 90° about Z, +X becomes +Y. Anything still sitting at x = 0.2 means the offset
            // was added without being rotated into the hand's space.
            Assert.AreEqual(0f, marker.localPosition.x, Tolerance,
                "A quarter turn about Z must move the offset off the X axis.");
            Assert.AreEqual(1f + offset.x, marker.localPosition.y, Tolerance,
                "The offset lands along +Y, above the hand's own metre.");
            Assert.AreEqual(0f, marker.localPosition.z, Tolerance);

            Assert.AreEqual(90f, marker.localRotation.eulerAngles.z, 1e-2f,
                "The marker inherits the followed part's rotation.");
        }

        /// <summary>
        /// A socket whose binding matches nothing reports unresolved and sits at the origin.
        /// </summary>
        /// <remarks>
        /// This is the state the window marks <c>(unresolved)</c> in the hierarchy, and the reason
        /// it does: at run time the attachment resolves to the actor's origin, which reads as a
        /// weapon lying at the character's feet with no obvious cause. Sitting the marker there too
        /// is the honest preview — it is where the thing will actually be.
        /// </remarks>
        [Test]
        public void SocketWithNoResolvableBinding_IsReportedAndSitsAtTheOrigin()
        {
            RigAsset rig = BuildRig(4242u, new Vector3(0.2f, 0f, 0f), Vector3.zero);

            // Rebind the socket to a target the rig does not declare.
            rig.sockets[0].targetId = 999u;

            rigMirror = new PreviewRigMirror();
            rigMirror.Rebuild(rig);

            socketMarkers = new PreviewSocketMarkers();
            socketMarkers.Rebuild(rig, null);
            socketMarkers.UpdateMarkers(rigMirror, null);

            Assert.IsFalse(
                socketMarkers.IsResolved(rig.sockets[0], rigMirror, null),
                "A binding that names no part must report as unresolved.");

            Transform marker = socketMarkers.GetMarker(77u);
            Assert.IsNotNull(marker, "An unresolved socket still gets a marker — that is the point.");
            Assert.AreEqual(0.2f, marker.localPosition.x, Tolerance,
                "With nothing to follow it sits at the origin plus its own offset, which is where "
                + "the runtime will put the attachment.");
            Assert.AreEqual(0f, marker.localPosition.y, Tolerance);
        }
    }
}
