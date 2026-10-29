using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class DoorZoneTests
    {
        private World testWorld;
        private EntityManager entityManager;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("DoorZoneTests");
            entityManager = testWorld.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        // Owner call: who opens a zone is set per zone. A player-only zone ignores minions stood in it.
        [Test]
        public void PlayerOnlyZone_OpensForThePlayer_NotForAMinion()
        {
            Entity door = CreateDoor();
            CreateZone(new float3(10f, 0f, 0f), DoorOpeners.Player, door, Entity.Null);
            Entity minion = CreateUnit(new float3(10f, 0f, 0.5f), isMinion: true);
            Entity player = CreatePlayer(new float3(-50f, 0f, 0f));

            UpdateZones();
            Assert.IsFalse(entityManager.GetComponentData<Door>(door).zoneWantsOpen, "A minion opened a player-only zone");

            entityManager.SetComponentData(player, LocalToWorld(new float3(10.5f, 0f, -0.5f)));
            UpdateZones();
            Assert.IsTrue(entityManager.GetComponentData<Door>(door).zoneWantsOpen);

            entityManager.SetComponentData(player, LocalToWorld(new float3(-50f, 0f, 0f)));
            UpdateZones();
            Assert.IsFalse(entityManager.GetComponentData<Door>(door).zoneWantsOpen, "The door should close once the player leaves");
            Assert.IsTrue(entityManager.Exists(minion));
        }

        // Owner call: while the vehicle is being driven it can't be opened or entered.
        [Test]
        public void ZoneOnADrivenVehicle_OpensNothing()
        {
            Entity vehicle = entityManager.CreateEntity(typeof(CaravanDriving));
            Entity door = CreateDoor();
            CreateZone(float3.zero, DoorOpeners.Player, door, vehicle);
            CreatePlayer(float3.zero);

            UpdateZones();
            Assert.IsFalse(entityManager.GetComponentData<Door>(door).zoneWantsOpen);

            entityManager.SetComponentEnabled<CaravanDriving>(vehicle, false);
            UpdateZones();
            Assert.IsTrue(entityManager.GetComponentData<Door>(door).zoneWantsOpen);
        }

        private Entity CreateDoor()
        {
            Entity door = entityManager.CreateEntity(typeof(Door));
            entityManager.SetComponentData(door, new Door { phase = DoorPhase.Closed, openSeconds = 0.5f, closeSeconds = 0.5f });
            return door;
        }

        private void CreateZone(float3 position, DoorOpeners openers, Entity door, Entity lockingVehicle)
        {
            Entity zone = entityManager.CreateEntity(typeof(DoorZone), typeof(LocalToWorld));
            entityManager.SetComponentData(zone, new DoorZone
            {
                halfExtents = new float3(1f, 1f, 1f),
                openers = openers,
                lockingVehicle = lockingVehicle,
            });
            entityManager.SetComponentData(zone, LocalToWorld(position));
            entityManager.AddBuffer<DoorZoneDoor>(zone).Add(new DoorZoneDoor { door = door });
        }

        private Entity CreatePlayer(float3 position)
        {
            Entity player = entityManager.CreateEntity(typeof(Player), typeof(LocalToWorld));
            entityManager.SetComponentData(player, LocalToWorld(position));
            return player;
        }

        private Entity CreateUnit(float3 position, bool isMinion)
        {
            Entity unit = entityManager.CreateEntity(typeof(Faction), typeof(Minion), typeof(LocalToWorld));
            entityManager.SetComponentEnabled<Minion>(unit, isMinion);
            entityManager.SetComponentData(unit, LocalToWorld(position));
            return unit;
        }

        private static LocalToWorld LocalToWorld(float3 position)
        {
            return new LocalToWorld { Value = float4x4.Translate(position) };
        }

        private void UpdateZones()
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<DoorZoneSystem>();
            handle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
        }
    }
}
