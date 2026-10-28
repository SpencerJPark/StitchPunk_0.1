using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class VehicleDamageTests
    {
        private const float CruisingSpeed = 6f;
        private const int HeavyHitThreshold = 30;
        private const int FullStopHit = 120;

        private World testWorld;
        private DamageBus damageBus;
        private BlobAssetReference<VehicleLibraryBlob> vehicleLibrary;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("VehicleDamageTests");
            EntityManager entityManager = testWorld.EntityManager;

            damageBus = new DamageBus
            {
                raw = new NativeQueue<DamageEvent>(Allocator.Persistent),
                resolved = new NativeQueue<DamageEvent>(Allocator.Persistent),
            };
            entityManager.CreateSingleton(damageBus);

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref VehicleLibraryBlob root = ref builder.ConstructRoot<VehicleLibraryBlob>();
            BlobBuilderArray<VehicleProfileBlob> vehicles = builder.Allocate(ref root.vehicles, 2);
            vehicles[1].vehicleType = VehicleType.Caravan;
            vehicles[1].thrownDamageMultiplier = 0.1f;
            vehicles[1].heavyHitDamageThreshold = HeavyHitThreshold;
            vehicles[1].fullStopHitDamage = FullStopHit;
            builder.Allocate(ref vehicles[0].driverSeatOffsets, 0);
            builder.Allocate(ref vehicles[0].pullerOffsets, 0);
            builder.Allocate(ref vehicles[1].driverSeatOffsets, 0);
            builder.Allocate(ref vehicles[1].pullerOffsets, 0);
            vehicleLibrary = builder.CreateBlobAssetReference<VehicleLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            entityManager.CreateSingleton(new VehicleLibrary { library = vehicleLibrary });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            damageBus.raw.Dispose();
            damageBus.resolved.Dispose();
            if (vehicleLibrary.IsCreated) vehicleLibrary.Dispose();
        }

        // Owner call: a rock is a scratch to a vehicle and never touches its momentum, however hard it's thrown.
        [Test]
        public void ThrownRock_IsScaledDown_AndNeverSlowsTheVehicle()
        {
            Entity caravan = CreateMovingCaravan();

            DamageEvent delivered = HitAndCollect(caravan, DamageSource.Throw, 200);

            Assert.AreEqual(20, delivered.damageAmount);
            Assert.AreEqual(CruisingSpeed, SpeedOf(caravan), 0.0001f);
        }

        // Small hits pass straight through; a heavy one costs momentum in proportion between the threshold and a full stop.
        [Test]
        public void MeleeHits_BelowThresholdDoNothing_HeavyHitsSlowItInProportion()
        {
            Entity caravan = CreateMovingCaravan();

            DamageEvent smallHit = HitAndCollect(caravan, DamageSource.Punch, HeavyHitThreshold - 1);
            Assert.AreEqual(HeavyHitThreshold - 1, smallHit.damageAmount, "Melee damage to a vehicle should not be scaled");
            Assert.AreEqual(CruisingSpeed, SpeedOf(caravan), 0.0001f);

            int halfwayHit = (HeavyHitThreshold + FullStopHit) / 2;
            HitAndCollect(caravan, DamageSource.Swing, halfwayHit);
            Assert.AreEqual(CruisingSpeed * 0.5f, SpeedOf(caravan), 0.05f);

            HitAndCollect(caravan, DamageSource.Swing, FullStopHit);
            Assert.AreEqual(0f, SpeedOf(caravan), 0.0001f);
        }

        private Entity CreateMovingCaravan()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity caravan = entityManager.CreateEntity(typeof(Vehicle), typeof(CaravanDriveState));
            entityManager.SetComponentData(caravan, new Vehicle { vehicleType = VehicleType.Caravan });
            entityManager.SetComponentData(caravan, new CaravanDriveState { forwardSpeed = CruisingSpeed });
            return caravan;
        }

        private DamageEvent HitAndCollect(Entity target, DamageSource damageSource, int damageAmount)
        {
            damageBus.resolved.Enqueue(new DamageEvent { targetEntity = target, damageSource = damageSource, damageAmount = damageAmount });
            SystemHandle handle = testWorld.GetOrCreateSystem<VehicleDamageSystem>();
            handle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
            Assert.AreEqual(1, damageBus.resolved.Count, "Every event must be put back on the bus");
            return damageBus.resolved.Dequeue();
        }

        private float SpeedOf(Entity caravan)
        {
            return testWorld.EntityManager.GetComponentData<CaravanDriveState>(caravan).forwardSpeed;
        }
    }
}
