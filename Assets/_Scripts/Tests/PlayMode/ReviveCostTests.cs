using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class ReviveCostTests
    {
        private World testWorld;
        private BlobAssetReference<UnitLibraryBlob> unitLibraryBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ReviveCostTests");
            unitLibraryBlob = BuildReviveLibrary();
            testWorld.EntityManager.AddComponentData(
                testWorld.EntityManager.CreateEntity(), new UnitDataLibrary { library = unitLibraryBlob });
            testWorld.SetTime(new TimeData(1.0, 0.016f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (unitLibraryBlob.IsCreated) unitLibraryBlob.Dispose();
        }

        [Test]
        public void RevivableCorpse_WithOneRound_SpendsOneRoundAndEnablesRevive()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity corpse = CreateCorpse(UnitType.MaleCitizen);
            Entity player = CreatePlayer(corpse, 1);

            RunReviverSystem();

            Assert.IsTrue(entityManager.IsComponentEnabled<ReviveRequest>(corpse));
            DynamicBuffer<ResourceChangeRequest> requests = entityManager.GetBuffer<ResourceChangeRequest>(player);
            Assert.AreEqual(1, requests.Length);
            Assert.AreEqual(ResourceType.RevivalRound, requests[0].resourceType);
            Assert.AreEqual(ResourceChangeType.Add, requests[0].changeType);
            Assert.AreEqual(-1, requests[0].value);
            Assert.IsFalse(entityManager.IsComponentEnabled<OnPlayerReviverEquip>(player));
        }

        [Test]
        public void RevivableCorpse_WithNoRounds_StampsShortfallAndDoesNotRevive()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity corpse = CreateCorpse(UnitType.MaleCitizen);
            Entity player = CreatePlayer(corpse, 0);

            RunReviverSystem();

            Assert.IsFalse(entityManager.IsComponentEnabled<ReviveRequest>(corpse));
            Assert.AreEqual(0, entityManager.GetBuffer<ResourceChangeRequest>(player).Length);
            Assert.IsTrue(entityManager.IsComponentEnabled<ResourceShortfall>(player));
            ResourceShortfall shortfall = entityManager.GetComponentData<ResourceShortfall>(player);
            Assert.AreEqual(ResourceType.RevivalRound, shortfall.resourceType);
            Assert.AreEqual(1, shortfall.missing);
        }

        [Test]
        public void NonRevivableCorpse_WithOneRound_CostsNothingAndDoesNotRevive()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity corpse = CreateCorpse(UnitType.PlayerZombie);
            Entity player = CreatePlayer(corpse, 1);

            RunReviverSystem();

            Assert.IsFalse(entityManager.IsComponentEnabled<ReviveRequest>(corpse));
            Assert.AreEqual(0, entityManager.GetBuffer<ResourceChangeRequest>(player).Length);
        }

        private void RunReviverSystem()
        {
            SystemHandle reviverHandle = testWorld.GetOrCreateSystem<PlayerReviverSystem>();
            reviverHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private Entity CreateCorpse(UnitType unitType)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity corpse = entityManager.CreateEntity(typeof(UnitData), typeof(ReviveRequest));
            entityManager.SetComponentData(corpse, new UnitData { unitType = unitType });
            entityManager.SetComponentEnabled<ReviveRequest>(corpse, false);
            return corpse;
        }

        private Entity CreatePlayer(Entity targetEntity, int revivalRounds)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = entityManager.CreateEntity(
                typeof(Player), typeof(OnPlayerReviverEquip), typeof(Target),
                typeof(ResourceStack), typeof(ResourceChangeRequest), typeof(ResourceShortfall));
            entityManager.SetComponentEnabled<OnPlayerReviverEquip>(player, true);
            entityManager.SetComponentData(player, new Target { entity = targetEntity });
            entityManager.SetComponentEnabled<Target>(player, true);
            entityManager.SetComponentEnabled<ResourceShortfall>(player, false);
            if (revivalRounds > 0)
            {
                entityManager.GetBuffer<ResourceStack>(player).Add(
                    new ResourceStack { resourceType = ResourceType.RevivalRound, amount = revivalRounds });
            }
            return player;
        }

        private static BlobAssetReference<UnitLibraryBlob> BuildReviveLibrary()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref UnitLibraryBlob root = ref builder.ConstructRoot<UnitLibraryBlob>();
            BlobBuilderArray<UnitDataBlob> unitsBuilder = builder.Allocate(ref root.units, 2);

            ConfigureUnit(builder, ref unitsBuilder[0], UnitType.MaleCitizen, UnitType.PlayerZombie);
            ConfigureUnit(builder, ref unitsBuilder[1], UnitType.PlayerZombie, UnitType.None);

            return builder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
        }

        private static void ConfigureUnit(BlobBuilder builder, ref UnitDataBlob unit, UnitType unitType, UnitType becomesUnitType)
        {
            unit.unitType = unitType;
            unit.becomesUnitType = becomesUnitType;
            builder.Allocate(ref unit.motivation, 0);
            builder.Allocate(ref unit.randomMotivations, 0);
            builder.Allocate(ref unit.attackFactions, 0);
            builder.Allocate(ref unit.socialFactions, 0);
            builder.Allocate(ref unit.attacks, 0);
            builder.Allocate(ref unit.actionAnimationKeys, 0);
            builder.Allocate(ref unit.faceAnimationKeys, 0);
            builder.Allocate(ref unit.stanceAnimationKeys, 0);
        }
    }
}
