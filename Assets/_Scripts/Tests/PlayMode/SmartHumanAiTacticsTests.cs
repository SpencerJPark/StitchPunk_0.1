using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class SmartHumanAiTacticsTests
    {
        private World testWorld;
        private List<IDisposable> persistentResources;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SmartHumanAiTacticsTests");
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
        public void KeepRange_TooCloseThrowerBacksOffToPreferredRange()
        {
            Entity unit = CreateThrowerSetup(out Entity target);

            RunSystem<RangedAttackAwarenessSystem>();

            DynamicBuffer<UtilityActions> actions = testWorld.EntityManager.GetBuffer<UtilityActions>(unit);
            Assert.AreEqual(1, CountActionsOfType(actions, ActionType.KeepRange));
            UtilityActions keepRange = actions[0];
            Assert.AreEqual(ActionType.KeepRange, keepRange.actionType);
            Assert.AreEqual(AIPriorityTier.Critical, keepRange.priority);
            Assert.IsTrue(keepRange.hasTargetPosition);
            float3 targetPosition = testWorld.EntityManager.GetComponentData<LocalTransform>(target).Position;
            float expectedRange = math.lerp(3f, 12f, RangedAttackAwarenessSystem.KEEP_RANGE_PREFERRED_FRACTION);
            Assert.AreEqual(expectedRange, math.distance(keepRange.targetPosition, targetPosition), 0.05f);
            Assert.Less(keepRange.targetPosition.x, targetPosition.x);
        }

        [Test]
        public void KeepRange_NotReofferedInsideCooldown()
        {
            Entity unit = CreateThrowerSetup(out Entity target);

            RunSystem<RangedAttackAwarenessSystem>();
            DynamicBuffer<UtilityActions> actions = testWorld.EntityManager.GetBuffer<UtilityActions>(unit);
            Assert.AreEqual(1, CountActionsOfType(actions, ActionType.KeepRange));
            actions.Clear();

            RunSystem<RangedAttackAwarenessSystem>();

            actions = testWorld.EntityManager.GetBuffer<UtilityActions>(unit);
            Assert.AreEqual(0, CountActionsOfType(actions, ActionType.KeepRange));
        }

        [Test]
        public void HealWhenHurt_BumpsToCriticalMidFightWithHostileAtDistance()
        {
            Entity unit = CreateHurtUnitWithBandageAndAttacker(attackerDistance: 5f);

            RunSystem<ItemAwarenessSystem>();

            DynamicBuffer<UtilityActions> actions = testWorld.EntityManager.GetBuffer<UtilityActions>(unit);
            Assert.AreEqual(1, CountActionsOfType(actions, ActionType.UseHealingItem));
            Assert.AreEqual(AIPriorityTier.Critical, FindAction(actions, ActionType.UseHealingItem).priority);
        }

        [Test]
        public void HealWhenHurt_StaysAmbientWithHostileInArmsReach()
        {
            Entity unit = CreateHurtUnitWithBandageAndAttacker(attackerDistance: 2f);

            RunSystem<ItemAwarenessSystem>();

            DynamicBuffer<UtilityActions> actions = testWorld.EntityManager.GetBuffer<UtilityActions>(unit);
            Assert.AreEqual(1, CountActionsOfType(actions, ActionType.UseHealingItem));
            Assert.AreEqual(0, FindAction(actions, ActionType.UseHealingItem).priority);
        }

        [Test]
        public void Retreat_BraveOutnumberedUnitFallsBackToSaferAlly_CowardDoesNot()
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateBrainLibrarySingleton(ActionType.Retreat);

            NativeParallelMultiHashMap<byte, Entity> registry = new NativeParallelMultiHashMap<byte, Entity>(8, Allocator.Persistent);
            persistentResources.Add(registry);
            entityManager.AddComponentData(entityManager.CreateEntity(), new FactionRegistry { entities = registry });

            Entity hostileNear = CreateRegisteredFactionMember(registry, FactionType.Army, new float3(3f, 0f, 0f));
            CreateRegisteredFactionMember(registry, FactionType.Army, new float3(3f, 0f, 1f));
            float3 allyPosition = new float3(-12f, 0f, 0f);
            CreateRegisteredFactionMember(registry, FactionType.Bandits, allyPosition);

            Entity braveUnit = CreateRetreatCandidate(hostileNear, bravery: 0.9f);
            Entity cowardUnit = CreateRetreatCandidate(hostileNear, bravery: 0.0f);

            RunSystem<RetreatAwarenessSystem>();

            DynamicBuffer<UtilityActions> braveActions = entityManager.GetBuffer<UtilityActions>(braveUnit);
            Assert.AreEqual(1, CountActionsOfType(braveActions, ActionType.Retreat));
            UtilityActions retreat = FindAction(braveActions, ActionType.Retreat);
            Assert.AreEqual(AIPriorityTier.Critical, retreat.priority);
            Assert.IsTrue(retreat.hasTargetPosition);
            Assert.Less(math.distance(retreat.targetPosition, allyPosition), 2f);

            DynamicBuffer<UtilityActions> cowardActions = entityManager.GetBuffer<UtilityActions>(cowardUnit);
            Assert.AreEqual(0, CountActionsOfType(cowardActions, ActionType.Retreat));
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<TSystem>();
            handle.Update(testWorld.Unmanaged);
        }

        private static int CountActionsOfType(DynamicBuffer<UtilityActions> actions, ActionType actionType)
        {
            int count = 0;
            for (int index = 0; index < actions.Length; index++)
                if (actions[index].actionType == actionType) count++;
            return count;
        }

        private static UtilityActions FindAction(DynamicBuffer<UtilityActions> actions, ActionType actionType)
        {
            for (int index = 0; index < actions.Length; index++)
                if (actions[index].actionType == actionType) return actions[index];
            Assert.Fail("No " + actionType + " option in the buffer");
            return default;
        }

        private void CreateBrainLibrarySingleton(params ActionType[] banditActionTypes)
        {
            EntityManager entityManager = testWorld.EntityManager;
            int unitTypeCount = Enum.GetValues(typeof(UnitType)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref BrainLibraryBlob root = ref builder.ConstructRoot<BrainLibraryBlob>();
            BlobBuilderArray<ActionDefBlob> actionDefs = builder.Allocate(ref root.actionDefs, banditActionTypes.Length);
            for (int index = 0; index < banditActionTypes.Length; index++)
            {
                actionDefs[index].actionType = banditActionTypes[index];
                builder.Allocate(ref actionDefs[index].considerations, 0);
            }
            BlobBuilderArray<BrainBlob> brains = builder.Allocate(ref root.brains, unitTypeCount);
            for (int index = 0; index < unitTypeCount; index++)
                builder.Allocate(ref brains[index].actionDefIndices, 0);
            BlobBuilderArray<int> banditIndices = builder.Allocate(ref brains[(int)UnitType.Bandit].actionDefIndices, banditActionTypes.Length);
            for (int index = 0; index < banditActionTypes.Length; index++) banditIndices[index] = index;
            BlobAssetReference<BrainLibraryBlob> brainLibrary = builder.CreateBlobAssetReference<BrainLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            persistentResources.Add(brainLibrary);
            entityManager.AddComponentData(entityManager.CreateEntity(), new BrainLibrary { blob = brainLibrary });
        }

        private void CreateItemLibrarySingleton(ItemType itemType, ItemBlob itemBlob)
        {
            EntityManager entityManager = testWorld.EntityManager;
            int itemTypeCount = Enum.GetValues(typeof(ItemType)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref ItemLibraryBlob root = ref builder.ConstructRoot<ItemLibraryBlob>();
            BlobBuilderArray<ItemBlob> items = builder.Allocate(ref root.items, itemTypeCount);
            itemBlob.itemType = itemType;
            items[(int)itemType] = itemBlob;
            BlobAssetReference<ItemLibraryBlob> itemLibrary = builder.CreateBlobAssetReference<ItemLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            persistentResources.Add(itemLibrary);
            entityManager.AddComponentData(entityManager.CreateEntity(), new ItemLibrary { library = itemLibrary });
        }

        private Entity CreateBanditUnit(float3 position, params ComponentType[] extraComponents)
        {
            EntityManager entityManager = testWorld.EntityManager;
            ComponentType[] baseComponents =
            {
                typeof(UtilityBrain), typeof(ActionRequest), typeof(Dead), typeof(CutsceneActor), typeof(Faction),
                typeof(LocalTransform), typeof(StateMachine), typeof(UtilityActions), typeof(Motivation)
            };
            ComponentType[] allComponents = new ComponentType[baseComponents.Length + extraComponents.Length];
            baseComponents.CopyTo(allComponents, 0);
            extraComponents.CopyTo(allComponents, baseComponents.Length);
            Entity unit = entityManager.CreateEntity(allComponents);
            entityManager.SetComponentEnabled<Dead>(unit, false);
            entityManager.SetComponentEnabled<CutsceneActor>(unit, false);
            entityManager.SetComponentData(unit, new UtilityBrain { unitType = UnitType.Bandit });
            entityManager.SetComponentData(unit, new Faction { factionType = FactionType.Bandits });
            entityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
            return unit;
        }

        private Entity CreateThrowerSetup(out Entity target)
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateBrainLibrarySingleton(ActionType.KeepRange);
            CreateItemLibrarySingleton(ItemType.Rock, new ItemBlob { aiRangedMinRange = 3f, aiRangedMaxRange = 12f });

            target = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(target, LocalTransform.FromPosition(new float3(1.5f, 0f, 0f)));
            Entity rock = entityManager.CreateEntity(typeof(Item));
            entityManager.SetComponentData(rock, new Item { itemType = ItemType.Rock });

            Entity unit = CreateBanditUnit(float3.zero, typeof(UnitEquip), typeof(TacticsCooldown));
            entityManager.SetComponentData(unit, new UnitEquip { equipItemEntity = rock, socketEntity = Entity.Null });
            entityManager.SetComponentData(unit, new StateMachine { action = ActionType.MeleeSingle, targetEntity = target });
            return unit;
        }

        private Entity CreateHurtUnitWithBandageAndAttacker(float attackerDistance)
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateBrainLibrarySingleton(ActionType.UseHealingItem);
            CreateItemLibrarySingleton(ItemType.MedKit, new ItemBlob { category = ItemCategory.Consumable, consumeEffect = EffectType.Healing });

            int effectTypeCount = Enum.GetValues(typeof(EffectType)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref EffectLibraryBlob root = ref builder.ConstructRoot<EffectLibraryBlob>();
            BlobBuilderArray<EffectBlob> effects = builder.Allocate(ref root.effects, effectTypeCount);
            effects[(int)EffectType.Healing].effectType = EffectType.Healing;
            effects[(int)EffectType.Healing].value = 50f;
            for (int index = 0; index < effectTypeCount; index++)
                builder.Allocate(ref effects[index].behaviours, 0);
            BlobAssetReference<EffectLibraryBlob> effectLibrary = builder.CreateBlobAssetReference<EffectLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            persistentResources.Add(effectLibrary);
            entityManager.AddComponentData(entityManager.CreateEntity(), new EffectLibrary { library = effectLibrary });

            NativeParallelMultiHashMap<Entity, Entity> occupancy = new NativeParallelMultiHashMap<Entity, Entity>(4, Allocator.Persistent);
            persistentResources.Add(occupancy);
            entityManager.AddComponentData(entityManager.CreateEntity(), new TargetOccupancy { occupantsByTarget = occupancy });
            entityManager.AddComponentData(entityManager.CreateEntity(), new LoggingConfig { EnabledCategories = 0 });

            float3 bandagePosition = new float3(4f, 0f, 0f);
            Entity bandage = entityManager.CreateEntity(typeof(Item), typeof(EquipBy), typeof(LocalTransform));
            entityManager.SetComponentData(bandage, new Item { itemType = ItemType.MedKit });
            entityManager.SetComponentData(bandage, new EquipBy { owner = Entity.Null });
            entityManager.SetComponentData(bandage, LocalTransform.FromPosition(bandagePosition));

            NativeParallelMultiHashMap<int2, Entity> itemCells = new NativeParallelMultiHashMap<int2, Entity>(4, Allocator.Persistent);
            persistentResources.Add(itemCells);
            itemCells.Add(InteractionSpatialHashSystem.GetCell(bandagePosition), bandage);
            entityManager.AddComponentData(entityManager.CreateEntity(), new SpatialHashRegistry { itemCells = itemCells });

            Entity attacker = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(attacker, LocalTransform.FromPosition(new float3(0f, 0f, attackerDistance)));

            Entity unit = CreateBanditUnit(float3.zero, typeof(Awareness), typeof(Health), typeof(ThreatEntry), typeof(PersonalityAttributes));
            entityManager.SetComponentData(unit, new Awareness { range = 20f });
            entityManager.SetComponentData(unit, new Health { healthAmount = 30, healthAmountMax = 100 });
            entityManager.GetBuffer<ThreatEntry>(unit).Add(new ThreatEntry { attackerEntity = attacker, threatScore = 10f, staleTimer = 5f });
            return unit;
        }

        private Entity CreateRegisteredFactionMember(NativeParallelMultiHashMap<byte, Entity> registry, FactionType factionType, float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity member = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(member, LocalTransform.FromPosition(position));
            registry.Add((byte)factionType, member);
            return member;
        }

        private Entity CreateRetreatCandidate(Entity attacker, float bravery)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = CreateBanditUnit(float3.zero, typeof(Awareness), typeof(Health), typeof(PersonalityAttributes),
                typeof(ThreatEntry), typeof(AttackFaction));
            entityManager.SetComponentData(unit, new Awareness { range = 20f });
            entityManager.SetComponentData(unit, new Health { healthAmount = 20, healthAmountMax = 100 });
            entityManager.GetBuffer<PersonalityAttributes>(unit).Add(new PersonalityAttributes { personality = PersonalityTypes.Bravery, value = bravery });
            entityManager.GetBuffer<ThreatEntry>(unit).Add(new ThreatEntry { attackerEntity = attacker, threatScore = 10f, staleTimer = 5f });
            entityManager.GetBuffer<AttackFaction>(unit).Add(new AttackFaction { faction = FactionType.Army });
            return unit;
        }
    }
}
