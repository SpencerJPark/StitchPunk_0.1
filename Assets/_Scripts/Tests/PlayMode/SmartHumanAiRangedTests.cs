using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class SmartHumanAiRangedTests
    {
        private const BehaviorType WindUpTestBehaviorType = BehaviorType.Wander;
        private const float TickSeconds = 1f / 60f;

        private World testWorld;
        private DamageBus damageBus;
        private BlobAssetReference<ItemLibraryBlob> itemLibrary;
        private BlobAssetReference<BehaviorLibraryBlob> behaviorLibraryBlob;
        private BlobAssetReference<UnitLibraryBlob> unitLibraryBlob;
        private NativeParallelMultiHashMap<int2, Entity> waypointCells;
        private double elapsedSeconds;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SmartHumanAiRangedTests");
            elapsedSeconds = 10.0;
            testWorld.SetTime(new TimeData(elapsedSeconds, TickSeconds));
            testWorld.EntityManager.CreateEntity(typeof(GameSceneTag));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (damageBus.raw.IsCreated) damageBus.raw.Dispose();
            if (damageBus.resolved.IsCreated) damageBus.resolved.Dispose();
            if (itemLibrary.IsCreated) itemLibrary.Dispose();
            if (behaviorLibraryBlob.IsCreated) behaviorLibraryBlob.Dispose();
            if (unitLibraryBlob.IsCreated) unitLibraryBlob.Dispose();
            if (waypointCells.IsCreated) waypointCells.Dispose();
        }

        [Test]
        public void ThrownRock_HitsSameFactionUnitInItsPath()
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateItemLibrarySingleton(10f, 3f);
            damageBus = new DamageBus
            {
                raw = new NativeQueue<DamageEvent>(Allocator.Persistent),
                resolved = new NativeQueue<DamageEvent>(Allocator.Persistent),
            };
            entityManager.CreateSingleton(damageBus);

            Entity thrower = CreateBanditWithHealth(float3.zero);
            Entity ally = CreateBanditWithHealth(new float3(0f, 0f, 3f));
            Entity rock = entityManager.CreateEntity(typeof(Item), typeof(LocalTransform), typeof(ThrownItemRequest), typeof(PlayerInteractable));
            entityManager.SetComponentData(rock, new Item { itemType = ItemType.Rock });
            entityManager.SetComponentData(rock, LocalTransform.FromPosition(new float3(0f, 0f, 3.1f)));
            entityManager.SetComponentData(rock, new ThrownItemRequest
            {
                velocity = new float3(0f, 0f, 14f),
                thrower = thrower,
                throwOrigin = new float3(0f, 1f, 0f),
            });
            entityManager.SetComponentEnabled<PlayerInteractable>(rock, false);

            RunSystem<ThrownItemHitSystem>();

            Assert.AreEqual(1, damageBus.raw.Count);
            DamageEvent damageEvent = damageBus.raw.Dequeue();
            Assert.AreEqual(ally, damageEvent.targetEntity);
            Assert.IsFalse(entityManager.IsComponentEnabled<ThrownItemRequest>(rock));
        }

        [Test]
        public void AiThrow_FliesAtLockedAimPoint_NotTheTargetsLivePosition()
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateItemLibrarySingleton(10f, 3f);

            Entity target = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(target, LocalTransform.FromPosition(new float3(10f, 0f, 0f)));
            Entity unit = entityManager.CreateEntity(typeof(AiThrowRequest), typeof(UnitEquip), typeof(LocalTransform));
            entityManager.SetComponentData(unit, new AiThrowRequest
            {
                target = target,
                aimPoint = new float3(0f, 0f, 10f),
                hasAimPoint = true,
            });
            entityManager.SetComponentData(unit, LocalTransform.Identity);
            Entity rock = entityManager.CreateEntity(typeof(Item), typeof(EquipBy), typeof(AttachedTo), typeof(Parent),
                typeof(LocalTransform), typeof(ThrownItemRequest), typeof(PlayerInteractable), typeof(Gravity));
            entityManager.SetComponentData(rock, new Item { itemType = ItemType.Rock });
            entityManager.SetComponentData(rock, new EquipBy { owner = unit });
            entityManager.SetComponentData(rock, new Parent { Value = unit });
            entityManager.SetComponentEnabled<ThrownItemRequest>(rock, false);
            entityManager.SetComponentEnabled<PlayerInteractable>(rock, false);
            entityManager.SetComponentData(unit, new UnitEquip { equipItemEntity = rock, socketEntity = Entity.Null });

            RunSystem<AiThrowItemSystem>();

            Assert.IsTrue(entityManager.IsComponentEnabled<ThrownItemRequest>(rock));
            float3 velocity = entityManager.GetComponentData<ThrownItemRequest>(rock).velocity;
            float2 horizontalDirection = math.normalize(velocity.xz);
            Assert.Greater(math.dot(horizontalDirection, new float2(0f, 1f)), 0.99f);
        }

        [Test]
        public void WindUpPresentation_FlashesDuringWindUpAndClearsOnRelease()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity part = entityManager.CreateEntity(typeof(BodyPartFlash));
            entityManager.SetComponentData(part, new BodyPartFlash { Value = new float4(1f, 1f, 1f, 0f) });
            Entity unit = entityManager.CreateEntity(typeof(RangedAim), typeof(BodyPart));
            entityManager.SetComponentData(unit, new RangedAim
            {
                aimPoint = new float3(0f, 0f, 5f),
                windUpTotal = 1f,
                windUpRemaining = 0.25f,
            });
            entityManager.GetBuffer<BodyPart>(unit).Add(new BodyPart { entity = part });
            Entity visual = entityManager.CreateEntity(typeof(VisualBillboard), typeof(Parent), typeof(LocalTransform));
            entityManager.SetComponentData(visual, new Parent { Value = unit });
            entityManager.SetComponentData(visual, LocalTransform.Identity);

            RunSystem<WindUpPresentationSystem>();

            float flashAlpha = entityManager.GetComponentData<BodyPartFlash>(part).Value.w;
            Assert.Greater(flashAlpha, 0f);
            Assert.LessOrEqual(flashAlpha, WindUpPresentationSystem.WIND_UP_FLASH_MAX);
            Assert.Greater(entityManager.GetComponentData<LocalTransform>(visual).Scale, 1f);

            entityManager.SetComponentEnabled<RangedAim>(unit, false);
            RunSystem<WindUpPresentationSystem>();

            Assert.AreEqual(0f, entityManager.GetComponentData<BodyPartFlash>(part).Value.w);
            Assert.AreEqual(1f, entityManager.GetComponentData<LocalTransform>(visual).Scale);
        }

        [Test]
        public void WindUp_LocksAimOnFirstFrameAndHoldsItWhileTargetMoves()
        {
            EntityManager entityManager = testWorld.EntityManager;
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            behaviorLibraryBlob = BuildWindUpBehaviorLibrary();
            unitLibraryBlob = BuildEmptyUnitLibrary();
            waypointCells = new NativeParallelMultiHashMap<int2, Entity>(1, Allocator.Persistent);

            Entity configEntity = entityManager.CreateEntity();
            entityManager.AddComponent<GameSceneTag>(configEntity);
            entityManager.AddComponentData(configEntity, new BehaviorLibrary { blob = behaviorLibraryBlob });
            entityManager.AddComponentData(configEntity, new UnitDataLibrary { library = unitLibraryBlob });
            entityManager.AddComponentData(configEntity, new SpatialHashRegistry { waypointCells = waypointCells });

            Entity target = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(target, LocalTransform.FromPosition(new float3(0f, 0f, 5f)));

            Entity unit = entityManager.CreateEntity();
            entityManager.AddComponentData(unit, LocalTransform.Identity);
            entityManager.AddComponentData(unit, new StateMachine
            {
                action = ActionType.Wander,
                activeBehavior = WindUpTestBehaviorType,
                currentPhase = BehaviorPhase.Execute,
                targetEntity = target,
            });
            entityManager.AddComponentData(unit, new PathRequest());
            entityManager.AddBuffer<RecentWaypoint>(unit);
            entityManager.AddBuffer<RecentInteraction>(unit);
            entityManager.AddBuffer<AvailableAttack>(unit);
            entityManager.AddBuffer<MotivationChangeRequest>(unit);
            entityManager.AddComponentData(unit, new UtilityBrain());
            entityManager.AddComponentData(unit, new RangedAim());
            entityManager.SetComponentEnabled<RangedAim>(unit, false);
            entityManager.AddComponentData(unit, new UnitEquip { equipItemEntity = Entity.Null, socketEntity = Entity.Null });

            RunBehaviorExecutionTick();

            Assert.IsTrue(entityManager.IsComponentEnabled<RangedAim>(unit));
            RangedAim lockedAim = entityManager.GetComponentData<RangedAim>(unit);
            Assert.AreEqual(new float3(0f, 0f, 5f), lockedAim.aimPoint);
            Assert.AreEqual(0.5f, lockedAim.windUpTotal);

            entityManager.SetComponentData(target, LocalTransform.FromPosition(new float3(5f, 0f, 0f)));
            RunBehaviorExecutionTick();
            Assert.AreEqual(new float3(0f, 0f, 5f), entityManager.GetComponentData<RangedAim>(unit).aimPoint);

            for (int tickIndex = 0; tickIndex < 60 && entityManager.GetComponentData<StateMachine>(unit).CurrentCommandIndex < 1; tickIndex++)
                RunBehaviorExecutionTick();

            Assert.AreEqual(1, entityManager.GetComponentData<StateMachine>(unit).CurrentCommandIndex);
            Assert.IsTrue(entityManager.IsComponentEnabled<RangedAim>(unit));
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<TSystem>();
            handle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void RunBehaviorExecutionTick()
        {
            elapsedSeconds += TickSeconds;
            testWorld.SetTime(new TimeData(elapsedSeconds, TickSeconds));
            RunSystem<BehaviorExecutionSystem>();
        }

        private Entity CreateBanditWithHealth(float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(typeof(Faction), typeof(Health), typeof(LocalTransform));
            entityManager.SetComponentData(unit, new Faction { factionType = FactionType.Bandits });
            entityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
            return unit;
        }

        private void CreateItemLibrarySingleton(float throwSpeed, float throwArc)
        {
            int itemTypeCount = System.Enum.GetValues(typeof(ItemType)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref ItemLibraryBlob root = ref builder.ConstructRoot<ItemLibraryBlob>();
            BlobBuilderArray<ItemBlob> items = builder.Allocate(ref root.items, itemTypeCount);
            items[(int)ItemType.Rock] = new ItemBlob { itemType = ItemType.Rock, throwSpeed = throwSpeed, throwArc = throwArc, throwDamage = 10 };
            itemLibrary = builder.CreateBlobAssetReference<ItemLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            testWorld.EntityManager.CreateSingleton(new ItemLibrary { library = itemLibrary });
        }

        private static BlobAssetReference<BehaviorLibraryBlob> BuildWindUpBehaviorLibrary()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref BehaviorLibraryBlob root = ref builder.ConstructRoot<BehaviorLibraryBlob>();
            BlobBuilderArray<BehaviorConfigBlob> behaviorsBuilder =
                builder.Allocate(ref root.behaviors, (int)WindUpTestBehaviorType + 1);

            for (int behaviorIndex = 0; behaviorIndex <= (int)WindUpTestBehaviorType; behaviorIndex++)
            {
                behaviorsBuilder[behaviorIndex].behaviorType = (BehaviorType)behaviorIndex;
                builder.Allocate(ref behaviorsBuilder[behaviorIndex].interruptionCleanup, 0);

                if (behaviorIndex != (int)WindUpTestBehaviorType)
                {
                    builder.Allocate(ref behaviorsBuilder[behaviorIndex].executionSequence, 0);
                    continue;
                }

                BlobBuilderArray<BehaviorCommand> executionSequence =
                    builder.Allocate(ref behaviorsBuilder[behaviorIndex].executionSequence, 2);
                executionSequence[0] = new BehaviorCommand { type = BehaviorCommandType.WindUp, FloatParam = 0.5f };
                executionSequence[1] = new BehaviorCommand { type = BehaviorCommandType.WaitTime, Duration = 5f };
            }

            return builder.CreateBlobAssetReference<BehaviorLibraryBlob>(Allocator.Persistent);
        }

        private static BlobAssetReference<UnitLibraryBlob> BuildEmptyUnitLibrary()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref UnitLibraryBlob root = ref builder.ConstructRoot<UnitLibraryBlob>();
            builder.Allocate(ref root.units, 0);
            return builder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
        }
    }
}
