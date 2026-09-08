using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public class PreviewOrbitCameraRigTests
    {
        [Test]
        public void LookAround_HoldsTheCameraPositionStill()
        {
            PreviewOrbitCameraRig rig = new PreviewOrbitCameraRig();
            rig.SetFrameTarget(new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f)));
            rig.ResetView();
            Vector3 positionBefore = rig.CameraPosition;
            rig.LookAround(new Vector2(30f, 10f));
            Vector3 positionAfter = rig.CameraPosition;
            Assert.Less(Vector3.Distance(positionBefore, positionAfter), 1e-4f);
        }

        [Test]
        public void Frame_BacksOffFarEnoughToHoldTheBounds()
        {
            PreviewOrbitCameraRig rig = new PreviewOrbitCameraRig();
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            rig.Frame(bounds);
            float halfFovRadians = 45f * 0.5f * Mathf.Deg2Rad;
            float minimumExpectedDistance = bounds.extents.magnitude / Mathf.Tan(halfFovRadians);
            Assert.GreaterOrEqual(rig.DebugOrbitDistance, minimumExpectedDistance - 1e-3f);
            Assert.GreaterOrEqual(rig.DebugOrbitDistance, 1f); // MinimumOrbitDistance
        }
    }
}
