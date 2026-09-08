using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public class PreviewCameraNavigationTests
    {
        private sealed class StubRig : IPreviewCameraRig
        {
            public readonly List<string> CallNames = new List<string>();
            public int FlyCallCount;
            public Vector3 LastFlyDirection;

            public void Orbit(Vector2 pixelDelta) { CallNames.Add("Orbit"); }
            public void Zoom(float amount) { CallNames.Add("Zoom"); }
            public void Pan(Vector2 pixelDelta, float viewportHeightPixels) { CallNames.Add("Pan"); }
            public void LookAround(Vector2 pixelDelta) { CallNames.Add("LookAround"); }
            public void Dolly(Vector2 pixelDelta) { CallNames.Add("Dolly"); }

            public void Fly(Vector3 localDirection, float deltaSeconds, bool fast)
            {
                CallNames.Add("Fly");
                FlyCallCount++;
                LastFlyDirection = localDirection;
            }

            public void ResetView() { CallNames.Add("ResetView"); }
            public void FrameSelection() { CallNames.Add("FrameSelection"); }
        }

        [Test]
        public void ResolveExclusiveGesture_MapsButtonsLikeTheSceneView()
        {
            Assert.AreEqual(PreviewCameraNavigation.Gesture.Pan, PreviewCameraNavigation.ResolveExclusiveGesture(2, false));
            Assert.AreEqual(PreviewCameraNavigation.Gesture.Look, PreviewCameraNavigation.ResolveExclusiveGesture(1, false));
            Assert.AreEqual(PreviewCameraNavigation.Gesture.Dolly, PreviewCameraNavigation.ResolveExclusiveGesture(1, true));
            Assert.AreEqual(PreviewCameraNavigation.Gesture.None, PreviewCameraNavigation.ResolveExclusiveGesture(0, false));
            Assert.AreEqual(PreviewCameraNavigation.Gesture.None, PreviewCameraNavigation.ResolveExclusiveGesture(0, true)); // Alt+left is the pick-cycle modifier, not a camera gesture
        }

        [Test]
        public void StepFly_MovesOnlyWhileLooking_AndForgetsKeysWhenTheGestureEnds()
        {
            StubRig stub = new StubRig();
            PreviewCameraNavigation navigation = new PreviewCameraNavigation { Rig = stub };
            Assert.IsTrue(navigation.TryBeginExclusiveGesture(1, false, false));
            Assert.IsTrue(navigation.TryHandleKeyDown(KeyDownEvent.GetPooled('w', KeyCode.W, EventModifiers.None)));
            navigation.StepFly(0.1f);
            Assert.AreEqual(1, stub.FlyCallCount);
            Assert.Greater(stub.LastFlyDirection.z, 0f);
            navigation.EndGesture();
            navigation.StepFly(0.1f);
            Assert.AreEqual(1, stub.FlyCallCount, "no further Fly after the gesture ends");
        }
    }
}
