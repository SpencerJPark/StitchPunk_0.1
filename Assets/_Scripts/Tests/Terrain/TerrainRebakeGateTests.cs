using System;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    // OnValidate fires on every load with nothing changed; the gate must raise only on a real serialized change.
    [TestFixture]
    public sealed class TerrainRebakeGateTests
    {
        private GameObject rampObject;
        private TerraceRamp ramp;

        [SetUp]
        public void CreateRamp()
        {
            if (TerrainRebakeGate.AutoRebakeKillSwitchEngaged)
            {
                Assert.Ignore("UserSettings/TerrainAutoRebake.off exists; the kill switch suppresses every event.");
            }

            rampObject = new GameObject("TerrainRebakeGateTestsRamp");
            ramp = rampObject.AddComponent<TerraceRamp>();
            TerrainRebakeGate.EndSettleForTests();
        }

        [TearDown]
        public void DestroyRamp()
        {
            if (rampObject != null) UnityEngine.Object.DestroyImmediate(rampObject);
        }

        [Test]
        public void RaisesOnlyWhenSerializedStateChanges()
        {
            Assert.IsFalse(TerrainRebakeGate.ShouldRaiseForValidate(ramp), "first sight must not raise");
            Assert.IsFalse(TerrainRebakeGate.ShouldRaiseForValidate(ramp), "unchanged must not raise");

            ramp.widthMetres += 1f;
            Assert.IsTrue(TerrainRebakeGate.ShouldRaiseForValidate(ramp), "a changed field must raise");
            Assert.IsFalse(TerrainRebakeGate.ShouldRaiseForValidate(ramp), "the change is raised once");
        }

        [Test]
        public void ChangeMadeUnderSuppressionIsNotRaisedLater()
        {
            TerrainRebakeGate.ShouldRaiseForValidate(ramp);

            using (IDisposable suppression = TerrainRebakeGate.Suppress())
            {
                ramp.widthMetres += 1f;
                Assert.IsFalse(TerrainRebakeGate.ShouldRaiseForValidate(ramp));
            }

            TerrainRebakeGate.EndSettleForTests();
            Assert.IsFalse(TerrainRebakeGate.ShouldRaiseForValidate(ramp));
        }
    }
}
