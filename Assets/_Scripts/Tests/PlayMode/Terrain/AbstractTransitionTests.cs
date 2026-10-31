using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // Tiles are 100 m. Tile (5,0) is where the unit starts and is never loaded; tile (1,0) is where it is walking when the player comes back.
    public sealed class AbstractTransitionTests
    {
        private const float UnitSpeedMetresPerSecond = 2f;
        private const float GroundTopY = 0.5f;
        private const int FramesPerUnitTurn = 10;

        private World testWorld;
        private SystemHandle transitionHandle;
        private Entity settingsEntity;
        private Entity clockEntity;
        private Entity unitEntity;
        private PhysicsWorld physicsWorld;
        private BlobAssetReference<Collider> groundCollider;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("AbstractTransitionTests");
            EntityManager entityManager = testWorld.EntityManager;

            settingsEntity = entityManager.CreateEntity(typeof(TerrainStreamingSettings));
            entityManager.SetComponentData(settingsEntity, new TerrainStreamingSettings { loadRadiusTiles = 1, unloadRadiusTiles = 2, maxLoadsPerFrame = 1 });
            DynamicBuffer<TerrainTileEntry> tileEntries = entityManager.AddBuffer<TerrainTileEntry>(settingsEntity);
            tileEntries.Add(CreateTileEntry(0, TerrainTileState.Loaded));
            tileEntries.Add(CreateTileEntry(1, TerrainTileState.Unloaded));
            tileEntries.Add(CreateTileEntry(5, TerrainTileState.Unloaded));

            Entity focusEntity = entityManager.CreateEntity(typeof(TerrainNavFocus), typeof(LocalTransform));
            entityManager.SetComponentData(focusEntity, LocalTransform.FromPosition(float3.zero));

            clockEntity = entityManager.CreateEntity(typeof(WorldClock));

            BuildGroundPhysicsWorld(entityManager);
            unitEntity = CreateUnit(entityManager, new float3(500f, 0f, 0f));

            transitionHandle = testWorld.GetOrCreateSystem<AbstractTransitionSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (physicsWorld.NumBodies > 0) physicsWorld.Dispose();
            if (groundCollider.IsCreated) groundCollider.Dispose();
        }

        [Test]
        public void AUnitOnAnUnloadedTileGoesAbstractWithARouteAndItsMovementOff()
        {
            RunOneTurnOfEveryUnit();

            Assert.IsTrue(IsEnabled<AbstractSimulation>());
            Assert.IsFalse(IsEnabled<Movement>());
            Assert.IsFalse(IsEnabled<Gravity>());
            AbstractSavedState savedState = testWorld.EntityManager.GetComponentData<AbstractSavedState>(unitEntity);
            Assert.IsTrue(savedState.hadMovementEnabled);
            Assert.IsTrue(savedState.hadGravityEnabled);
            Assert.AreEqual(2, testWorld.EntityManager.GetBuffer<AbstractRouteWaypoint>(unitEntity).Length);
            Assert.AreEqual(500f, testWorld.EntityManager.GetComponentData<AbstractRoute>(unitEntity).totalLengthMetres, 0.01f);
        }

        [Test]
        public void AnAbstractUnitStaysAbstractWhileItIsStillOnTilesThatAreNotLoaded()
        {
            RunOneTurnOfEveryUnit();

            // 100 s at 2 m/s puts it at x = 300, tile 3: a tile that has no loaded ground.
            SetWorldSeconds(100.0);
            RunOneTurnOfEveryUnit();

            Assert.IsTrue(IsEnabled<AbstractSimulation>());
        }

        [Test]
        public void AUnitMaterialisesAtItsRoutePositionWithItsMovementBitsRestored()
        {
            RunOneTurnOfEveryUnit();

            SetTileState(1, TerrainTileState.Loaded);
            SetWorldSeconds(220.0);
            RunOneTurnOfEveryUnit();

            EntityManager entityManager = testWorld.EntityManager;
            Assert.IsFalse(IsEnabled<AbstractSimulation>());
            Assert.IsTrue(IsEnabled<Movement>());
            Assert.IsTrue(IsEnabled<Gravity>());
            Assert.IsTrue(IsEnabled<PathRequest>());
            float3 position = entityManager.GetComponentData<LocalTransform>(unitEntity).Position;
            Assert.AreEqual(60f, position.x, 0.01f);
            Assert.AreEqual(GroundTopY, position.y, 0.01f);
            Assert.AreEqual(0f, entityManager.GetComponentData<PathRequest>(unitEntity).targetPosition.x, 0.01f);
        }

        [Test]
        public void AUnitWhoseMovementWasOffBeforeGoingAbstractStaysOffWhenItMaterialises()
        {
            testWorld.EntityManager.SetComponentEnabled<Movement>(unitEntity, false);
            RunOneTurnOfEveryUnit();

            SetTileState(1, TerrainTileState.Loaded);
            SetWorldSeconds(220.0);
            RunOneTurnOfEveryUnit();

            Assert.IsFalse(IsEnabled<AbstractSimulation>());
            Assert.IsFalse(IsEnabled<Movement>());
        }

        [Test]
        public void APinnedUnitNeverGoesAbstract()
        {
            testWorld.EntityManager.AddComponent<PinnedActive>(unitEntity);

            RunOneTurnOfEveryUnit();

            Assert.IsFalse(IsEnabled<AbstractSimulation>());
            Assert.IsTrue(IsEnabled<Movement>());
        }

        [Test]
        public void ADeadUnitIsLeftExactlyAsItIs()
        {
            testWorld.EntityManager.SetComponentEnabled<Dead>(unitEntity, true);
            testWorld.EntityManager.SetComponentEnabled<Movement>(unitEntity, false);

            RunOneTurnOfEveryUnit();

            Assert.IsFalse(IsEnabled<AbstractSimulation>());
            Assert.IsFalse(IsEnabled<Movement>());
        }

        private void RunOneTurnOfEveryUnit()
        {
            for (int frame = 0; frame < FramesPerUnitTurn; frame++)
                transitionHandle.Update(testWorld.Unmanaged);
        }

        private bool IsEnabled<T>() where T : unmanaged, IComponentData, IEnableableComponent
        {
            return testWorld.EntityManager.IsComponentEnabled<T>(unitEntity);
        }

        private void SetWorldSeconds(double totalWorldSeconds)
        {
            testWorld.EntityManager.SetComponentData(clockEntity, new WorldClock { totalWorldSeconds = totalWorldSeconds });
        }

        private void SetTileState(int tileX, TerrainTileState tileState)
        {
            DynamicBuffer<TerrainTileEntry> tileEntries = testWorld.EntityManager.GetBuffer<TerrainTileEntry>(settingsEntity);
            for (int index = 0; index < tileEntries.Length; index++)
            {
                if (tileEntries[index].coordinate.x != tileX)
                    continue;
                TerrainTileEntry tileEntry = tileEntries[index];
                tileEntry.state = tileState;
                tileEntries[index] = tileEntry;
            }
        }

        private static TerrainTileEntry CreateTileEntry(int tileX, TerrainTileState tileState)
        {
            return new TerrainTileEntry { coordinate = new int2(tileX, 0), state = tileState, sceneEntity = Entity.Null };
        }

        private void BuildGroundPhysicsWorld(EntityManager entityManager)
        {
            groundCollider = BoxCollider.Create(new BoxGeometry
            {
                Center = float3.zero,
                Orientation = quaternion.identity,
                Size = new float3(4000f, 1f, 4000f),
                BevelRadius = 0f
            });
            physicsWorld = new PhysicsWorld(1, 0, 0);
            NativeArray<RigidBody> bodies = physicsWorld.CollisionWorld.Bodies;
            bodies[0] = new RigidBody
            {
                WorldFromBody = RigidTransform.identity,
                Scale = 1f,
                Collider = groundCollider,
                Entity = Entity.Null,
                CustomTags = 0
            };
            physicsWorld.CollisionWorld.BuildBroadphase(ref physicsWorld, 1f / 60f, new float3(0f, -9.81f, 0f));

            Entity physicsEntity = entityManager.CreateEntity(typeof(PhysicsWorldSingleton));
            entityManager.SetComponentData(physicsEntity, new PhysicsWorldSingleton { PhysicsWorld = physicsWorld });
            Entity gridEntity = entityManager.CreateEntity(typeof(NavGridSettings));
            entityManager.SetComponentData(gridEntity, new NavGridSettings { groundLayerMask = ~0u });
        }

        private static Entity CreateUnit(EntityManager entityManager, float3 position)
        {
            Entity unit = entityManager.CreateEntity(
                typeof(LocalTransform), typeof(StateMachine), typeof(Movement), typeof(Gravity), typeof(PathRequest),
                typeof(DStarLiteFollower), typeof(FlowFieldFollower), typeof(AbstractSimulation), typeof(AbstractSavedState),
                typeof(AbstractRoute), typeof(AbstractRestAnchor), typeof(Dead));
            entityManager.AddBuffer<AbstractRouteWaypoint>(unit);

            entityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(unit, new StateMachine
            {
                action = ActionType.Patrol,
                hasTargetPosition = true,
                targetPosition = float3.zero
            });
            entityManager.SetComponentData(unit, new Movement { moveSpeed = UnitSpeedMetresPerSecond, isMoving = true });

            entityManager.SetComponentEnabled<PathRequest>(unit, false);
            entityManager.SetComponentEnabled<DStarLiteFollower>(unit, false);
            entityManager.SetComponentEnabled<FlowFieldFollower>(unit, false);
            entityManager.SetComponentEnabled<AbstractSimulation>(unit, false);
            entityManager.SetComponentEnabled<Dead>(unit, false);
            return unit;
        }
    }
}
