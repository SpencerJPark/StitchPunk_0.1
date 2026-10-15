using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class PlayerKnockoutSystemTests
    {
        private const int MaxHealth = 100;
        private World testWorld;
        private double elapsedSeconds;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("PlayerKnockoutSystemTests");
            elapsedSeconds = 10.0;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void ZeroHealthWithStation_KnocksOutAndMovesPlayerToStation()
        {
            Entity gameOver = CreateGameOverSingleton();
            Entity station = CreateStation(new float3(50f, 0f, 20f));
            Entity player = CreatePlayer(0, station);

            RunKnockoutSystem(0.1f);

            EntityManager entityManager = testWorld.EntityManager;
            Assert.IsTrue(entityManager.IsComponentEnabled<PlayerKnockedOut>(player));
            Assert.AreEqual(5f, entityManager.GetComponentData<PlayerKnockedOut>(player).remainingSeconds, 0.01f);
            Assert.IsFalse(entityManager.IsComponentEnabled<Dead>(player));
            Assert.IsFalse(entityManager.IsComponentEnabled<WorkstationMounted>(player));
            float3 summonSpawnPoint = new float3(50f, 0f, 22f);
            Assert.Less(math.distance(summonSpawnPoint, entityManager.GetComponentData<LocalTransform>(player).Position), 3f);
            Assert.IsFalse(entityManager.IsComponentEnabled<CampLoopGameOver>(gameOver));
        }

        [Test]
        public void KnockoutElapsed_WakesPlayerWithFullHealth()
        {
            CreateGameOverSingleton();
            Entity station = CreateStation(float3.zero);
            Entity player = CreatePlayer(0, station);
            RunKnockoutSystem(0.1f);

            RunKnockoutSystem(2f);
            RunKnockoutSystem(2f);
            RunKnockoutSystem(2f);

            EntityManager entityManager = testWorld.EntityManager;
            Assert.AreEqual(MaxHealth, entityManager.GetComponentData<Health>(player).healthAmount);
            Assert.IsFalse(entityManager.IsComponentEnabled<PlayerKnockedOut>(player));
        }

        [Test]
        public void ZeroHealthWithNoStation_TriggersGameOver()
        {
            Entity gameOver = CreateGameOverSingleton();
            Entity player = CreatePlayer(0, Entity.Null);

            RunKnockoutSystem(0.1f);

            EntityManager entityManager = testWorld.EntityManager;
            Assert.IsTrue(entityManager.IsComponentEnabled<CampLoopGameOver>(gameOver));
            Assert.IsFalse(entityManager.IsComponentEnabled<PlayerKnockedOut>(player));
        }

        [Test]
        public void PositiveHealth_ChangesNothing()
        {
            Entity gameOver = CreateGameOverSingleton();
            Entity station = CreateStation(new float3(50f, 0f, 20f));
            Entity player = CreatePlayer(40, station);

            RunKnockoutSystem(0.1f);

            EntityManager entityManager = testWorld.EntityManager;
            Assert.IsFalse(entityManager.IsComponentEnabled<PlayerKnockedOut>(player));
            Assert.IsFalse(entityManager.IsComponentEnabled<CampLoopGameOver>(gameOver));
            Assert.AreEqual(40, entityManager.GetComponentData<Health>(player).healthAmount);
            Assert.AreEqual(0f, entityManager.GetComponentData<LocalTransform>(player).Position.x, 0.001f);
        }

        private void RunKnockoutSystem(float deltaTime)
        {
            elapsedSeconds += deltaTime;
            testWorld.SetTime(new TimeData(elapsedSeconds, deltaTime));
            SystemHandle handle = testWorld.GetOrCreateSystem<PlayerKnockoutSystem>();
            handle.Update(testWorld.Unmanaged);
        }

        private Entity CreateGameOverSingleton()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity gameOver = entityManager.CreateEntity(typeof(CampLoopGameOver));
            entityManager.SetComponentEnabled<CampLoopGameOver>(gameOver, false);
            return gameOver;
        }

        private Entity CreateStation(float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity station = entityManager.CreateEntity(typeof(Workstation), typeof(LocalTransform));
            entityManager.SetComponentData(station, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(station, new Workstation
            {
                ownerPlayer = Entity.Null,
                mountLocalOffset = float3.zero,
                summonSpawnLocalOffset = new float3(0f, 0f, 2f),
                rallyLocalOffset = new float3(0f, 0f, 4f)
            });
            return station;
        }

        private Entity CreatePlayer(int healthAmount, Entity station)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = entityManager.CreateEntity(
                typeof(Player), typeof(LocalTransform), typeof(Health), typeof(Dead), typeof(PlayerKnockedOut),
                typeof(PlayerWorkstationLink), typeof(WorkstationMounted), typeof(MovePlayerInput));
            entityManager.SetComponentData(player, LocalTransform.Identity);
            entityManager.SetComponentData(player, new Health { healthAmount = healthAmount, healthAmountMax = MaxHealth });
            entityManager.SetComponentData(player, new PlayerWorkstationLink { workstation = station });
            entityManager.SetComponentEnabled<Dead>(player, healthAmount <= 0);
            entityManager.SetComponentEnabled<PlayerKnockedOut>(player, false);
            entityManager.SetComponentEnabled<WorkstationMounted>(player, true);
            entityManager.SetComponentEnabled<MovePlayerInput>(player, true);
            return player;
        }
    }
}
