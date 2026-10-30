using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.Tests.PlayMode
{
    // F2. The weekly roll is a pure function of (seed, week, reputation): same inputs -> same orders, and a template that
    // needs a higher tier is never offered to a client below it.
    public sealed class TradeRollTests
    {
        private const int ClientId = 1;
        private const int StrangerReputation = 10;
        private const int TrustedReputation = 60;
        private const int WeeksToSample = 40;

        private World testWorld;
        private EntityManager entityManager;
        private BlobAssetReference<ClientLibraryBlob> libraryBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TradeRollTests");
            entityManager = testWorld.EntityManager;

            using (BlobBuilder builder = new BlobBuilder(Allocator.Temp))
            {
                ref ClientLibraryBlob root = ref builder.ConstructRoot<ClientLibraryBlob>();
                root.tuning = new TradeTuningBlob
                {
                    payoutMultiplierByTier = new float4(1f),
                    minOffersByTier = new int4(1),
                    maxOffersByTier = new int4(1),
                    currencyPerDifficulty = 1f,
                    startingReputation = StrangerReputation,
                };

                BlobBuilderArray<ClientBlob> clients = builder.Allocate(ref root.clients, 1);
                clients[0].clientId = ClientId;
                clients[0].kind = ClientKind.Recurring;
                clients[0].baseOrderChancePerWeek = 1f;
                clients[0].tierThresholds = new int3(25, 50, 80);
                BlobBuilderArray<OrderTemplateBlob> pool = builder.Allocate(ref clients[0].orderPool, 2);
                FillTemplate(builder, ref pool[0], UnitType.PlayerZombieBrute, ReputationTier.Stranger);
                FillTemplate(builder, ref pool[1], UnitType.PlayerZombieRunner, ReputationTier.Trusted);
                builder.Allocate(ref root.household.templates, 0);

                libraryBlob = builder.CreateBlobAssetReference<ClientLibraryBlob>(Allocator.Persistent);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
                testWorld.Dispose();
            testWorld = null;
            if (libraryBlob.IsCreated)
                libraryBlob.Dispose();
        }

        private static void FillTemplate(BlobBuilder builder, ref OrderTemplateBlob template, UnitType unitType, ReputationTier minimumTier)
        {
            template.unitType = unitType;
            template.quantityRange = new int2(2, 4);
            template.dueDayRange = new int2(2, 5);
            template.minimumTier = minimumTier;
            template.weight = 1f;
            builder.Allocate(ref template.bonusResources, 0);
        }

        private DynamicBuffer<TradeOrder> RollWeek(int reputation, uint seed, int weekNumber)
        {
            Entity boardEntity = entityManager.CreateEntity(typeof(TradeOrder), typeof(ClientRelationship));
            entityManager.GetBuffer<ClientRelationship>(boardEntity).Add(new ClientRelationship { clientId = ClientId, reputation = reputation });

            using (NativeArray<TradeDropOff> noDropOffs = new NativeArray<TradeDropOff>(0, Allocator.Temp))
            {
                TradeOrderRoller.RollWeek(
                    ref libraryBlob.Value, default, noDropOffs,
                    entityManager.GetBuffer<TradeOrder>(boardEntity), entityManager.GetBuffer<ClientRelationship>(boardEntity),
                    seed, weekNumber);
            }

            return entityManager.GetBuffer<TradeOrder>(boardEntity);
        }

        [Test]
        public void SameSeedAndWeek_RollsTheSameOrders()
        {
            DynamicBuffer<TradeOrder> first = RollWeek(StrangerReputation, 4242u, 3);
            DynamicBuffer<TradeOrder> second = RollWeek(StrangerReputation, 4242u, 3);

            Assert.AreEqual(first.Length, second.Length);
            Assert.Greater(first.Length, 0);
            for (int orderIndex = 0; orderIndex < first.Length; orderIndex++)
            {
                Assert.AreEqual(first[orderIndex].orderId, second[orderIndex].orderId);
                Assert.AreEqual(first[orderIndex].templateIndex, second[orderIndex].templateIndex);
                Assert.AreEqual(first[orderIndex].quantity, second[orderIndex].quantity);
                Assert.AreEqual(first[orderIndex].daysAllowed, second[orderIndex].daysAllowed);
            }
        }

        [Test]
        public void TrustedOnlyTemplate_IsNeverOfferedToAStranger_ButIsOfferedToATrustedClient()
        {
            bool strangerSawRunner = false;
            bool trustedSawRunner = false;
            for (int weekNumber = 1; weekNumber <= WeeksToSample; weekNumber++)
            {
                DynamicBuffer<TradeOrder> strangerOrders = RollWeek(StrangerReputation, 99u, weekNumber);
                for (int orderIndex = 0; orderIndex < strangerOrders.Length; orderIndex++)
                    strangerSawRunner |= strangerOrders[orderIndex].unitType == UnitType.PlayerZombieRunner;

                DynamicBuffer<TradeOrder> trustedOrders = RollWeek(TrustedReputation, 99u, weekNumber);
                for (int orderIndex = 0; orderIndex < trustedOrders.Length; orderIndex++)
                    trustedSawRunner |= trustedOrders[orderIndex].unitType == UnitType.PlayerZombieRunner;
            }

            Assert.IsFalse(strangerSawRunner, "Runners need Trusted; a Stranger must only ever be offered Brutes.");
            Assert.IsTrue(trustedSawRunner, "Without this the test could pass by the roll never picking the second template at all.");
        }
    }
}
