using System;
using System.Collections.Generic;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class SmartHumanAiTests
    {
        private World testWorld;
        private List<IDisposable> persistentResources;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SmartHumanAiTests");
            testWorld.SetTime(new TimeData(10.0, 0.05f));
            persistentResources = new List<IDisposable>();
            testWorld.EntityManager.CreateEntity(typeof(GameSceneTag));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            foreach (IDisposable resource in persistentResources) resource.Dispose();
            persistentResources.Clear();
        }

        [Test]
        public void ThinkSchedule_BusyUnitThinksOnlyWhenTimerElapses()
        {
            Entity unit = CreateThinkingUnit(BehaviorType.Wander, 0.1f);

            RunSystem<ThinkScheduleSystem>();
            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<ActionRequest>(unit));

            RunSystem<ThinkScheduleSystem>();
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<ActionRequest>(unit));
        }

        [Test]
        public void ThinkSchedule_IdleUnitAlwaysThinks()
        {
            Entity unit = CreateThinkingUnit(BehaviorType.None, 10f);

            RunSystem<ThinkScheduleSystem>();

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<ActionRequest>(unit));
        }

        [Test]
        public void AllyAlert_SpotterAlertsSameFactionAllyInRadiusOnly()
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateAllyAlertSingletons(out NativeParallelMultiHashMap<byte, Entity> registry);

            Entity enemy = entityManager.CreateEntity(typeof(LocalTransform), typeof(Dead));
            entityManager.SetComponentData(enemy, LocalTransform.FromPosition(new float3(2f, 0f, 0f)));
            entityManager.SetComponentEnabled<Dead>(enemy, false);

            Entity spotter = CreateBanditUnit(float3.zero, typeof(RaiseAlarm));
            entityManager.SetComponentData(spotter, new RaiseAlarm { radius = 15f });
            entityManager.SetComponentData(spotter, new StateMachine { action = ActionType.MeleeSingle, targetEntity = enemy });
            Entity nearAlly = CreateBanditUnit(new float3(5f, 0f, 0f), typeof(AllyAlert), typeof(ThinkRate), typeof(CutsceneActor));
            Entity farAlly = CreateBanditUnit(new float3(30f, 0f, 0f), typeof(AllyAlert), typeof(ThinkRate), typeof(CutsceneActor));
            foreach (Entity ally in new Entity[] { nearAlly, farAlly })
            {
                entityManager.SetComponentEnabled<AllyAlert>(ally, false);
                entityManager.SetComponentEnabled<CutsceneActor>(ally, false);
                entityManager.SetComponentData(ally, new ThinkRate { interval = 0.25f, timeUntilNextThink = 0.2f });
            }
            registry.Add((byte)FactionType.Bandits, spotter);
            registry.Add((byte)FactionType.Bandits, nearAlly);
            registry.Add((byte)FactionType.Bandits, farAlly);

            RunSystem<AllyAlertSystem>();
            testWorld.EntityManager.CompleteAllTrackedJobs();

            Assert.IsTrue(entityManager.IsComponentEnabled<AllyAlert>(nearAlly));
            Assert.AreEqual(enemy, entityManager.GetComponentData<AllyAlert>(nearAlly).target);
            Assert.AreEqual(0f, entityManager.GetComponentData<ThinkRate>(nearAlly).timeUntilNextThink);
            Assert.IsFalse(entityManager.IsComponentEnabled<AllyAlert>(farAlly));
            Assert.Greater(entityManager.GetComponentData<RaiseAlarm>(spotter).cooldownRemaining, 0f);
        }

        [Test]
        public void AiThrow_LaunchesHeldItemTowardTarget()
        {
            EntityManager entityManager = testWorld.EntityManager;
            int itemTypeCount = Enum.GetValues(typeof(ItemType)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref ItemLibraryBlob root = ref builder.ConstructRoot<ItemLibraryBlob>();
            BlobBuilderArray<ItemBlob> items = builder.Allocate(ref root.items, itemTypeCount);
            items[(int)ItemType.Rock] = new ItemBlob { itemType = ItemType.Rock, throwSpeed = 10f, throwArc = 3f };
            BlobAssetReference<ItemLibraryBlob> itemLibrary = builder.CreateBlobAssetReference<ItemLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            persistentResources.Add(itemLibrary);
            Entity libraryEntity = entityManager.CreateEntity(typeof(ItemLibrary));
            entityManager.SetComponentData(libraryEntity, new ItemLibrary { library = itemLibrary });

            Entity target = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(target, LocalTransform.FromPosition(new float3(10f, 0f, 0f)));
            Entity unit = entityManager.CreateEntity(typeof(AiThrowRequest), typeof(UnitEquip), typeof(LocalTransform));
            entityManager.SetComponentData(unit, new AiThrowRequest { target = target });
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

            Assert.IsFalse(entityManager.IsComponentEnabled<AiThrowRequest>(unit));
            Assert.AreEqual(Entity.Null, entityManager.GetComponentData<UnitEquip>(unit).equipItemEntity);
            Assert.IsFalse(entityManager.HasComponent<Parent>(rock));
            Assert.IsTrue(entityManager.IsComponentEnabled<ThrownItemRequest>(rock));
            ThrownItemRequest thrown = entityManager.GetComponentData<ThrownItemRequest>(rock);
            Assert.Greater(thrown.velocity.x, 0f);
            Assert.Less(math.abs(thrown.velocity.z), 0.01f);
            Assert.AreEqual(unit, thrown.thrower);
            Assert.AreEqual(Entity.Null, entityManager.GetComponentData<EquipBy>(rock).owner);
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<TSystem>();
            handle.Update(testWorld.Unmanaged);
        }

        private Entity CreateThinkingUnit(BehaviorType activeBehavior, float timeUntilNextThink)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(typeof(UtilityBrain), typeof(ActionRequest), typeof(Dead), typeof(StateMachine), typeof(ThinkRate));
            entityManager.SetComponentEnabled<Dead>(unit, false);
            entityManager.SetComponentData(unit, new StateMachine { activeBehavior = activeBehavior });
            entityManager.SetComponentData(unit, new ThinkRate { interval = 0.25f, timeUntilNextThink = timeUntilNextThink });
            return unit;
        }

        private Entity CreateBanditUnit(float3 position, params ComponentType[] extraComponents)
        {
            EntityManager entityManager = testWorld.EntityManager;
            ComponentType[] baseComponents =
            {
                typeof(UtilityBrain), typeof(Dead), typeof(Faction), typeof(LocalTransform), typeof(StateMachine),
                typeof(UtilityActions), typeof(Motivation)
            };
            ComponentType[] allComponents = new ComponentType[baseComponents.Length + extraComponents.Length];
            baseComponents.CopyTo(allComponents, 0);
            extraComponents.CopyTo(allComponents, baseComponents.Length);
            Entity unit = entityManager.CreateEntity(allComponents);
            entityManager.SetComponentEnabled<Dead>(unit, false);
            entityManager.SetComponentData(unit, new Faction { factionType = FactionType.Bandits });
            entityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
            return unit;
        }

        private void CreateAllyAlertSingletons(out NativeParallelMultiHashMap<byte, Entity> registry)
        {
            EntityManager entityManager = testWorld.EntityManager;
            registry = new NativeParallelMultiHashMap<byte, Entity>(8, Allocator.Persistent);
            persistentResources.Add(registry);
            entityManager.AddComponentData(entityManager.CreateEntity(), new FactionRegistry { entities = registry });

            BlobBuilder unitBuilder = new BlobBuilder(Allocator.Temp);
            ref UnitLibraryBlob unitRoot = ref unitBuilder.ConstructRoot<UnitLibraryBlob>();
            unitBuilder.Allocate(ref unitRoot.units, 0);
            BlobAssetReference<UnitLibraryBlob> unitLibrary = unitBuilder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
            unitBuilder.Dispose();
            persistentResources.Add(unitLibrary);
            entityManager.AddComponentData(entityManager.CreateEntity(), new UnitDataLibrary { library = unitLibrary });

            BlobBuilder brainBuilder = new BlobBuilder(Allocator.Temp);
            ref BrainLibraryBlob brainRoot = ref brainBuilder.ConstructRoot<BrainLibraryBlob>();
            brainBuilder.Allocate(ref brainRoot.brains, 0);
            brainBuilder.Allocate(ref brainRoot.actionDefs, 0);
            BlobAssetReference<BrainLibraryBlob> brainLibrary = brainBuilder.CreateBlobAssetReference<BrainLibraryBlob>(Allocator.Persistent);
            brainBuilder.Dispose();
            persistentResources.Add(brainLibrary);
            entityManager.AddComponentData(entityManager.CreateEntity(), new BrainLibrary { blob = brainLibrary });
        }
    }
}
