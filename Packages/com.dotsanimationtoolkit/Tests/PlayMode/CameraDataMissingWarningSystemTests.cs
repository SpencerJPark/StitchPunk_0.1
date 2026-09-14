// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>CameraDataMissingWarningSystem</c>: one warning and a self-disable when a billboarded rig
    /// waits for a camera, and silence for AnimLod actors while distance LOD is off.
    /// </summary>
    public sealed class CameraDataMissingWarningSystemTests
    {
        // Mirrors the system's private grace period; the runtime assembly exposes no internals to tests.
        private const int FramesBeforeWarning = 120;

        private World testWorld;
        private SystemHandle warningSystem;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CameraDataMissingWarningSystemTests");
            warningSystem = testWorld.GetOrCreateSystem<CameraDataMissingWarningSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;
        }

        [Test]
        public void BillboardRigWithoutCamera_WarnsAtFrame120ThenDisables()
        {
            Entity billboardActor = testWorld.EntityManager.CreateEntity();
            testWorld.EntityManager.AddBuffer<BillboardRootElement>(billboardActor);

            UpdateWarningSystem(FramesBeforeWarning - 1);
            Assert.IsTrue(IsWarningSystemEnabled(), "the grace period has not run out yet");

            LogAssert.Expect(LogType.Warning, new Regex("AnimationToolkitCameraData"));
            UpdateWarningSystem(1);

            // Unity never fails a test on an unexpected warning, so "no repeat" is proven by the
            // system switching itself off, not by LogAssert.
            Assert.IsFalse(IsWarningSystemEnabled(), "the warning is logged once, then the system disables itself");
        }

        [Test]
        public void LodActorsWithDistanceLodOff_NeverWarn()
        {
            Entity lodActor = testWorld.EntityManager.CreateEntity();
            testWorld.EntityManager.AddComponentData(lodActor, new AnimLod { level = 0 });
            testWorld.EntityManager.CreateSingleton(new AnimationToolkitConfig { distanceLodEnabled = false });

            UpdateWarningSystem(FramesBeforeWarning * 2);

            Assert.IsTrue(IsWarningSystemEnabled(),
                "an AnimLod actor needs no camera unless distance LOD is enabled, so nothing warns or disables");
        }

        private void UpdateWarningSystem(int frameCount)
        {
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                warningSystem.Update(testWorld.Unmanaged);
            }
        }

        private bool IsWarningSystemEnabled()
        {
            return testWorld.Unmanaged.ResolveSystemStateRef(warningSystem).Enabled;
        }
    }
}
