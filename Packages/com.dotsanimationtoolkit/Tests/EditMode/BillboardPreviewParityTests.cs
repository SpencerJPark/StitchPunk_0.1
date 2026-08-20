// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The Clip Editor's viewport and the runtime job must resolve a billboard identically
    /// (amendment A44). <c>SocketPreviewParityTests</c> is the precedent; this is the same obligation
    /// for the other thing the viewport now shows that the game also computes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>What this can and cannot check.</strong> Both paths call
    /// <see cref="BillboardMath.TryResolve"/>, so they cannot disagree about the arithmetic — that
    /// is the design, and it is why neither owns a copy. What they <em>could</em> disagree about is
    /// the plumbing on either side of it: the preview writes a world rotation onto a
    /// <c>Transform</c> and lets Unity convert it into the parent's space, while the runtime does
    /// that conversion by hand with an inverse multiply. Those are two different pieces of code
    /// reaching the same place, and this fixture pins that they do.
    /// </para>
    /// </remarks>
    public sealed class BillboardPreviewParityTests
    {
        private const float Tolerance = 2e-3f;

        private Transform actorRoot;
        private Transform torso;
        private Transform hand;

        [SetUp]
        public void SetUp()
        {
            actorRoot = new GameObject("Actor").transform;
            torso = new GameObject("Torso").transform;
            hand = new GameObject("Hand").transform;
            torso.SetParent(actorRoot, false);
            hand.SetParent(torso, false);

            // Deliberately not axis-aligned, so a parent-space conversion that dropped a term would
            // show up rather than cancelling out.
            actorRoot.localPosition = new Vector3(3f, 1f, -2f);
            actorRoot.localRotation = Quaternion.Euler(0f, 35f, 0f);
            torso.localPosition = new Vector3(0f, 1.2f, 0f);
            torso.localRotation = Quaternion.Euler(10f, 0f, 5f);
            hand.localPosition = new Vector3(0.4f, 0.3f, 0f);
            hand.localRotation = Quaternion.Euler(0f, 0f, 20f);
        }

        [TearDown]
        public void TearDown()
        {
            if (actorRoot != null)
            {
                Object.DestroyImmediate(actorRoot.gameObject);
            }
        }

        private static BillboardSettings Settings(BillboardMode mode)
        {
            return new BillboardSettings
            {
                mode = mode,
                constraintAxis = new float3(0f, 1f, 0f),
                frozenYaw = 0f,
                angleOffsetRadians = 0f,
                blendWeight = 1f,
                enabled = true,
                snapSteps = 0,
                snapPhaseRadians = 0f,
                clampHalfArcRadians = -1f
            };
        }

        /// <summary>
        /// The preview's write: set the world rotation and let Unity convert.
        /// </summary>
        private static quaternion ResolveThePreviewWay(
            Transform node, in BillboardSettings settings, float3 cameraPosition, float3 cameraForward)
        {
            quaternion resolvedRotation;
            if (!BillboardMath.TryResolve(
                    settings, node.position, cameraPosition, cameraForward, node.rotation,
                    out resolvedRotation))
            {
                return node.localRotation;
            }
            node.rotation = resolvedRotation;
            return node.localRotation;
        }

        /// <summary>
        /// The runtime's write: convert the world result into the parent's space by hand, exactly as
        /// <c>ResolveBillboardRootsJob</c> does.
        /// </summary>
        private static quaternion ResolveTheRuntimeWay(
            Transform node, in BillboardSettings settings, float3 cameraPosition, float3 cameraForward)
        {
            quaternion nodeWorldRotation = node.rotation;
            quaternion resolvedRotation;
            if (!BillboardMath.TryResolve(
                    settings, node.position, cameraPosition, cameraForward, nodeWorldRotation,
                    out resolvedRotation))
            {
                return node.localRotation;
            }

            quaternion parentWorldRotation = node.parent != null
                ? node.parent.rotation
                : quaternion.identity;
            return math.mul(math.inverse(parentWorldRotation), resolvedRotation);
        }

        private void AssertParity(Transform node, BillboardMode mode, float3 cameraForward)
        {
            float3 cameraPosition = new float3(-4f, 6f, -9f);
            BillboardSettings settings = Settings(mode);

            quaternion runtimeLocal =
                ResolveTheRuntimeWay(node, in settings, cameraPosition, cameraForward);
            quaternion previewLocal =
                ResolveThePreviewWay(node, in settings, cameraPosition, cameraForward);

            float4 runtimeValue = math.normalize(runtimeLocal.value);
            float4 previewValue = math.normalize(previewLocal.value);
            if (math.dot(runtimeValue, previewValue) < 0f)
            {
                previewValue = -previewValue;
            }

            Assert.Less(
                math.length(runtimeValue - previewValue),
                Tolerance,
                "The viewport and the game must land on the same local rotation for "
                + mode + ", or the preview is answering a different question.");
        }

        [Test]
        public void ScreenAligned_ResolvesIdenticallyOnANestedNode()
        {
            AssertParity(hand, BillboardMode.ScreenAligned, math.normalize(new float3(0.3f, -0.2f, 1f)));
        }

        [Test]
        public void Full_ResolvesIdenticallyOnANestedNode()
        {
            AssertParity(hand, BillboardMode.Full, float3.zero);
        }

        [Test]
        public void Upright_ResolvesIdenticallyOnANestedNode()
        {
            AssertParity(hand, BillboardMode.Upright, float3.zero);
        }

        [Test]
        public void ARootWithNoParentResolvesIdenticallyToo()
        {
            AssertParity(actorRoot, BillboardMode.ScreenAligned, math.normalize(new float3(1f, 0f, 1f)));
        }

        /// <summary>
        /// The override case: an inner root must cancel the outer one rather than compose on top of
        /// it, in both paths, or a held item turns twice.
        /// </summary>
        [Test]
        public void ANestedRootUnderAnAlreadyBillboardedAncestor_MatchesInBothPaths()
        {
            float3 cameraPosition = new float3(-4f, 6f, -9f);
            float3 cameraForward = math.normalize(new float3(0.3f, -0.2f, 1f));
            BillboardSettings settings = Settings(BillboardMode.ScreenAligned);

            // Resolve the ancestor first, as depth ordering guarantees at runtime.
            ResolveThePreviewWay(torso, in settings, cameraPosition, cameraForward);

            quaternion runtimeLocal =
                ResolveTheRuntimeWay(hand, in settings, cameraPosition, cameraForward);
            quaternion previewLocal =
                ResolveThePreviewWay(hand, in settings, cameraPosition, cameraForward);

            float4 runtimeValue = math.normalize(runtimeLocal.value);
            float4 previewValue = math.normalize(previewLocal.value);
            if (math.dot(runtimeValue, previewValue) < 0f)
            {
                previewValue = -previewValue;
            }

            Assert.Less(math.length(runtimeValue - previewValue), Tolerance);

            // And the whole point of the cancellation: the inner root ends up facing the camera, not
            // facing it twice over.
            AssertRotationsEqual(
                quaternion.LookRotation(cameraForward, math.up()),
                hand.rotation,
                "A nested billboard must land on the same world facing as an unnested one.");
        }

        private static void AssertRotationsEqual(quaternion expected, quaternion actual, string because)
        {
            float4 expectedValue = math.normalize(expected.value);
            float4 actualValue = math.normalize(actual.value);
            if (math.dot(expectedValue, actualValue) < 0f)
            {
                actualValue = -actualValue;
            }
            Assert.Less(math.length(expectedValue - actualValue), Tolerance, because);
        }
    }
}
