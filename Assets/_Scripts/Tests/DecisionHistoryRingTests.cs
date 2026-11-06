using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests
{
    // The inspector reads this ring as "last N decisions, oldest first"; a wrong capacity or order silently misreports history.
    [TestFixture]
    public sealed class DecisionHistoryRingTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private Entity historyEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("DecisionHistoryRingTests");
            entityManager = testWorld.EntityManager;
            historyEntity = entityManager.CreateEntity();
            entityManager.AddBuffer<DecisionHistoryEntry>(historyEntity);
        }

        [TearDown]
        public void TearDown()
        {
            testWorld.Dispose();
        }

        private void AppendEntries(int count)
        {
            DynamicBuffer<DecisionHistoryEntry> history = entityManager.GetBuffer<DecisionHistoryEntry>(historyEntity);
            for (int index = 0; index < count; index++)
            {
                DecisionHistoryRing.Append(ref history, new DecisionHistoryEntry { worldSeconds = index });
            }
        }

        [Test]
        public void Append_PastCapacity_KeepsNewestTwentyInOrder()
        {
            AppendEntries(25);

            DynamicBuffer<DecisionHistoryEntry> history = entityManager.GetBuffer<DecisionHistoryEntry>(historyEntity);
            Assert.AreEqual(20, history.Length);
            Assert.AreEqual(5d, history[0].worldSeconds);
            Assert.AreEqual(24d, history[history.Length - 1].worldSeconds);
            for (int index = 1; index < history.Length; index++)
            {
                Assert.Greater(history[index].worldSeconds, history[index - 1].worldSeconds);
            }
        }

        [Test]
        public void Append_UnderCapacity_KeepsEveryEntryInOrder()
        {
            AppendEntries(7);

            DynamicBuffer<DecisionHistoryEntry> history = entityManager.GetBuffer<DecisionHistoryEntry>(historyEntity);
            Assert.AreEqual(7, history.Length);
            for (int index = 0; index < history.Length; index++)
            {
                Assert.AreEqual((double)index, history[index].worldSeconds);
            }
        }
    }
}
