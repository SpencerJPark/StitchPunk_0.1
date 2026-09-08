// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public sealed class ClipPreviewControllerPoseTests
    {
        private ClipPreviewController controller;

        [SetUp]
        public void CreateFixture()
        {
            controller = new ClipPreviewController();
        }

        [TearDown]
        public void DestroyFixture()
        {
            controller.Dispose();
        }

        [Test]
        public void CapturePose_ThenRestorePose_PutsFocusAndDistanceBack()
        {
            controller.Pan(new Vector2(40f, 0f), 400f);
            controller.Zoom(3f);
            controller.Orbit(new Vector2(30f, 10f));
            PreviewCameraPose capturedPose = controller.CapturePose();

            controller.ResetView();
            controller.RestorePose(in capturedPose);

            PreviewCameraPose restoredPose = controller.CapturePose();
            Assert.AreEqual(capturedPose.yawDegrees, restoredPose.yawDegrees, 1e-4f);
            Assert.AreEqual(capturedPose.pitchDegrees, restoredPose.pitchDegrees, 1e-4f);
            Assert.AreEqual(capturedPose.distance, restoredPose.distance, 1e-4f);
            Assert.AreEqual(capturedPose.focus.x, restoredPose.focus.x, 1e-4f);
            Assert.AreEqual(capturedPose.focus.y, restoredPose.focus.y, 1e-4f);
            Assert.AreEqual(capturedPose.focus.z, restoredPose.focus.z, 1e-4f);
        }
    }
}
