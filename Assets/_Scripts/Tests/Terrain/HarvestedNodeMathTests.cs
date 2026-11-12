using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class HarvestedNodeMathTests
    {
        private const double SecondsPerGameDay = 600.0;

        [Test]
        public void IsRespawnDue_OneSecondEarly_IsFalse_AndAtExactlyDue_IsTrue()
        {
            double harvestedAt = 100.0;
            double dueAt = harvestedAt + 2f * SecondsPerGameDay;

            Assert.IsFalse(HarvestedNodeMath.IsRespawnDue(harvestedAt, dueAt - 1.0, 2f, SecondsPerGameDay));
            Assert.IsTrue(HarvestedNodeMath.IsRespawnDue(harvestedAt, dueAt, 2f, SecondsPerGameDay));
        }

        [Test]
        public void IsRespawnDue_ZeroRespawnDays_NeverRespawns()
        {
            Assert.IsFalse(HarvestedNodeMath.IsRespawnDue(0.0, 1.0e12, 0f, SecondsPerGameDay));
        }

        [Test]
        public void IsRespawnDue_ZeroSecondsPerGameDay_NeverRespawns()
        {
            Assert.IsFalse(HarvestedNodeMath.IsRespawnDue(0.0, 1.0e12, 2f, 0.0));
        }

        [Test]
        public void IndexOfKey_AbsentKey_ReturnsMinusOne_AndPresentKey_ReturnsItsIndex()
        {
            World testWorld = new World("HarvestedNodeMathTests");
            try
            {
                EntityManager manager = testWorld.EntityManager;
                Entity ledgerEntity = manager.CreateEntity();
                DynamicBuffer<HarvestedNodeLedgerEntry> ledger = manager.AddBuffer<HarvestedNodeLedgerEntry>(ledgerEntity);
                ledger.Add(new HarvestedNodeLedgerEntry { nodeKey = 11u, harvestedAtWorldSeconds = 1.0 });
                ledger.Add(new HarvestedNodeLedgerEntry { nodeKey = 22u, harvestedAtWorldSeconds = 2.0 });

                Assert.AreEqual(-1, HarvestedNodeMath.IndexOfKey(in ledger, 33u));
                Assert.AreEqual(1, HarvestedNodeMath.IndexOfKey(in ledger, 22u));
            }
            finally
            {
                testWorld.Dispose();
            }
        }
    }
}
