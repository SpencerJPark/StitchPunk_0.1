// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class ImportedClipLaneResolverTests
    {
        [Test]
        public void Resolve_OneRowPerAnimatedPath_KeyTimesNormalisedByTheClipDuration()
        {
            AnimationClip sourceClip = new AnimationClip();
            try
            {
                AnimationCurve hipsArmPositionXCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f));
                AnimationCurve hipsArmPositionYCurve = new AnimationCurve(new Keyframe(0.5f, 0f), new Keyframe(1.0f, 1f));
                AnimationCurve hipsLegPositionZCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.6f, 1f), new Keyframe(1.2f, 0f));

                AnimationUtility.SetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve("Hips/Arm", typeof(Transform), "m_LocalPosition.x"), hipsArmPositionXCurve);
                AnimationUtility.SetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve("Hips/Arm", typeof(Transform), "m_LocalPosition.y"), hipsArmPositionYCurve);
                AnimationUtility.SetEditorCurve(sourceClip, EditorCurveBinding.FloatCurve("Hips/Leg", typeof(Transform), "m_LocalPosition.z"), hipsLegPositionZCurve);

                List<ImportedClipLane> resolvedLanes = ImportedClipLaneResolver.Resolve(sourceClip, 1.0f);

                Assert.AreEqual(2, resolvedLanes.Count);

                ImportedClipLane armLane = resolvedLanes[0];
                Assert.AreEqual("Hips/Arm", armLane.nodePath);
                Assert.AreEqual("Arm", armLane.displayName);
                Assert.AreEqual(0, armLane.keysPastClipEnd);
                CollectionAssert.AreEqual(new float[] { 0f, 0.5f, 1.0f }, armLane.normalizedKeyTimes, new FloatToleranceComparer());

                ImportedClipLane legLane = resolvedLanes[1];
                Assert.AreEqual("Hips/Leg", legLane.nodePath);
                Assert.AreEqual("Leg", legLane.displayName);
                Assert.AreEqual(1, legLane.keysPastClipEnd);
                CollectionAssert.AreEqual(new float[] { 0f, 0.6f }, legLane.normalizedKeyTimes, new FloatToleranceComparer());
            }
            finally
            {
                Object.DestroyImmediate(sourceClip);
            }
        }

        private sealed class FloatToleranceComparer : IComparer<float>, System.Collections.IComparer
        {
            public int Compare(float firstValue, float secondValue)
            {
                return Mathf.Abs(firstValue - secondValue) <= 1e-4f ? 0 : firstValue.CompareTo(secondValue);
            }

            int System.Collections.IComparer.Compare(object firstValue, object secondValue)
            {
                return Compare((float)firstValue, (float)secondValue);
            }
        }
    }
}
