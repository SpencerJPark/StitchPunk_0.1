// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using StitchPunk.AnimationToolkit.Authoring;
using StitchPunk.AnimationToolkit.Editor;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// The arithmetic behind keying and gizmo dragging — the parts with a right answer.
    /// </summary>
    /// <remarks>
    /// Scoped to the pure logic on purpose. Whether a handle is drawn in the right place is
    /// something a person can see at a glance; whether a drag lands where the pointer is depends on
    /// a closest-point solve that looks equally plausible with its sign inverted, and that one
    /// shipped inverted until a test caught it.
    /// </remarks>
    public sealed class ClipEditingLogicTests
    {
        private const float Tolerance = 1e-4f;

        // ---------------------------------------------------------------------------------
        // Gizmo drag arithmetic.
        // ---------------------------------------------------------------------------------

        [Test]
        public void ClosestAxisParameter_HasTheSignOfTheSideItIsOn()
        {
            // The regression this file exists for. A negated solve returns the right magnitude on
            // the wrong side, so a handle tracks backwards — visible immediately when dragging, and
            // invisible in any test that only checks the distance.
            Ray positiveRay = new Ray(new Vector3(3.5f, 0f, -5f), Vector3.forward);
            float positiveParameter;
            Assert.IsTrue(PreviewGizmoMath.TryGetClosestAxisParameter(
                positiveRay, Vector3.zero, Vector3.right, out positiveParameter));
            Assert.AreEqual(3.5f, positiveParameter, Tolerance);

            Ray negativeRay = new Ray(new Vector3(-2f, 0f, -5f), Vector3.forward);
            float negativeParameter;
            Assert.IsTrue(PreviewGizmoMath.TryGetClosestAxisParameter(
                negativeRay, Vector3.zero, Vector3.right, out negativeParameter));
            Assert.AreEqual(-2f, negativeParameter, Tolerance);
        }

        [Test]
        public void ClosestAxisParameter_IsMeasuredFromTheAxisOrigin()
        {
            Ray ray = new Ray(new Vector3(4f, 0f, -5f), Vector3.forward);
            float axisParameter;
            Assert.IsTrue(PreviewGizmoMath.TryGetClosestAxisParameter(
                ray, new Vector3(1f, 0f, 0f), Vector3.right, out axisParameter));
            Assert.AreEqual(
                3f, axisParameter, Tolerance,
                "A pivot away from the world origin must not offset the drag.");
        }

        [Test]
        public void ClosestAxisParameter_RefusesARayParallelToTheAxis()
        {
            Ray parallelRay = new Ray(new Vector3(0f, 1f, 0f), Vector3.right);
            float axisParameter;
            Assert.IsFalse(
                PreviewGizmoMath.TryGetClosestAxisParameter(
                    parallelRay, Vector3.zero, Vector3.right, out axisParameter),
                "Every point is equally close, so any answer would make the drag jump.");
        }

        [Test]
        public void PickHandle_ResolvesTheArmUnderTheRay()
        {
            Assert.AreEqual(
                GizmoHandle.AxisX,
                PreviewGizmoMath.PickHandle(
                    new Ray(new Vector3(2f, 0f, -5f), Vector3.forward),
                    GizmoMode.Move, Vector3.zero, 4f));
            Assert.AreEqual(
                GizmoHandle.AxisY,
                PreviewGizmoMath.PickHandle(
                    new Ray(new Vector3(0f, 2f, -5f), Vector3.forward),
                    GizmoMode.Move, Vector3.zero, 4f));
            Assert.AreEqual(
                GizmoHandle.None,
                PreviewGizmoMath.PickHandle(
                    new Ray(new Vector3(3f, 3f, -5f), Vector3.forward),
                    GizmoMode.Move, Vector3.zero, 4f));
        }

        [Test]
        public void PickHandle_RotateHitsTheRingRatherThanItsCentre()
        {
            Assert.AreEqual(
                GizmoHandle.RotateZ,
                PreviewGizmoMath.PickHandle(
                    new Ray(new Vector3(4f, 0f, -5f), Vector3.forward),
                    GizmoMode.Rotate, Vector3.zero, 4f));
            Assert.AreEqual(
                GizmoHandle.None,
                PreviewGizmoMath.PickHandle(
                    new Ray(new Vector3(0.2f, 0f, -5f), Vector3.forward),
                    GizmoMode.Rotate, Vector3.zero, 4f),
                "The middle of the ring is empty space, not a handle.");
        }

        // ---------------------------------------------------------------------------------
        // Keying and sampling.
        // ---------------------------------------------------------------------------------

        private static TransformTrack BuildTwoKeyTrack()
        {
            TransformTrack track = new TransformTrack { targetId = 1u };
            track.keys.Add(new TransformKey
            {
                normalizedTime = 0f,
                position = float3.zero,
                rotation = new float3(0f, 0f, 0f),
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });
            track.keys.Add(new TransformKey
            {
                normalizedTime = 1f,
                position = new float3(4f, 2f, 0f),
                rotation = new float3(0f, 0f, 90f),
                scale = new float3(2f, 2f, 1f),
                interpolation = Interpolation.Linear
            });
            return track;
        }

        [Test]
        public void Evaluate_InterpolatesBetweenKeys()
        {
            float3 position;
            float3 rotationDegrees;
            float3 scale;
            Assert.IsTrue(ClipTransformEditing.TryEvaluate(
                BuildTwoKeyTrack(), 0.5f, out position, out rotationDegrees, out scale));

            Assert.AreEqual(2f, position.x, Tolerance);
            Assert.AreEqual(1f, position.y, Tolerance);
            Assert.AreEqual(45f, rotationDegrees.z, Tolerance);
            Assert.AreEqual(1.5f, scale.x, Tolerance);
        }

        [Test]
        public void Evaluate_ClampsOutsideTheKeyedRange()
        {
            TransformTrack track = BuildTwoKeyTrack();
            float3 position;
            float3 rotationDegrees;
            float3 scale;

            ClipTransformEditing.TryEvaluate(track, -1f, out position, out rotationDegrees, out scale);
            Assert.AreEqual(0f, position.x, Tolerance);

            ClipTransformEditing.TryEvaluate(track, 2f, out position, out rotationDegrees, out scale);
            Assert.AreEqual(4f, position.x, Tolerance);
        }

        [Test]
        public void Evaluate_HoldsTheLeftKeyThroughASteppedSegment()
        {
            TransformTrack track = BuildTwoKeyTrack();
            TransformKey steppedKey = track.keys[0];
            steppedKey.interpolation = Interpolation.Step;
            track.keys[0] = steppedKey;

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            ClipTransformEditing.TryEvaluate(track, 0.9f, out position, out rotationDegrees, out scale);
            Assert.AreEqual(0f, position.x, Tolerance);
        }

        [Test]
        public void SetKeyValues_UpdatesTheKeyAlreadyAtThatTime()
        {
            TransformTrack track = BuildTwoKeyTrack();
            int keyIndex = ClipTransformEditing.SetKeyValues(
                track, 1f, new float3(9f, 9f, 0f), new float3(0f, 0f, 12f), new float3(3f, 3f, 1f));

            Assert.AreEqual(2, track.keys.Count, "Keying an existing time must not add a key.");
            Assert.AreEqual(9f, track.keys[keyIndex].position.x, Tolerance);
        }

        [Test]
        public void SetKeyValues_InsertsInTimeOrder()
        {
            TransformTrack track = BuildTwoKeyTrack();
            ClipTransformEditing.SetKeyValues(
                track, 0.5f, new float3(1f, 1f, 0f), new float3(0f, 0f, 30f), new float3(1f, 1f, 1f));

            Assert.AreEqual(3, track.keys.Count);
            Assert.AreEqual(0f, track.keys[0].normalizedTime, Tolerance);
            Assert.AreEqual(0.5f, track.keys[1].normalizedTime, Tolerance);
            Assert.AreEqual(1f, track.keys[2].normalizedTime, Tolerance);
        }

        [Test]
        public void SetKeyValues_InheritsTheInterpolationOfTheSegmentItLandsIn()
        {
            TransformTrack track = BuildTwoKeyTrack();
            TransformKey steppedKey = track.keys[0];
            steppedKey.interpolation = Interpolation.Step;
            track.keys[0] = steppedKey;

            int keyIndex = ClipTransformEditing.SetKeyValues(
                track, 0.5f, new float3(1f, 1f, 0f), new float3(0f, 0f, 30f), new float3(1f, 1f, 1f));

            Assert.AreEqual(
                Interpolation.Step, track.keys[keyIndex].interpolation,
                "Keying inside a stepped run must not silently turn that segment linear.");
        }

        [Test]
        public void FindKeyIndexAt_ToleratesFloatingPointPlayheadDrift()
        {
            TransformTrack track = BuildTwoKeyTrack();
            Assert.AreEqual(
                0,
                ClipTransformEditing.FindKeyIndexAt(track, 1e-6f),
                "A playhead a millionth off a key is on that key by any standard a user has.");
            Assert.AreEqual(-1, ClipTransformEditing.FindKeyIndexAt(track, 0.5f));
        }
    }
}
