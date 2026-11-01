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
    public sealed class SmartHumanAiGunTests
    {
        private const float PISTOL_MINIMUM_RANGE = 4f;
        private const float PISTOL_MAXIMUM_RANGE = 14f;
        private const float PISTOL_SPEED = 24f;
        private const float PISTOL_COOLDOWN_SECONDS = 1.6f;
        private const int   PISTOL_DAMAGE = 25;

        private World testWorld;
        private List<IDisposable> persistentResources;
        private DamageBus damageBus;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SmartHumanAiGunTests");
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
            if (damageBus.raw.IsCreated) damageBus.raw.Dispose();
            if (damageBus.resolved.IsCreated) damageBus.resolved.Dispose();
            damageBus = default;
        }

        [Test]
        public void Shoot_OfferedOnlyInsideGunRangeAndAfterRangedCooldown()
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateBrainLibrarySingleton(ActionType.Shoot, ActionType.KeepRange);
            CreateItemLibrarySingleton(PistolBlob());
            Entity target = entityManager.CreateEntity(typeof(LocalTransform));
            Entity shooter = CreateGunman(target);

            AssertShootOffers(shooter, target, new float3(8f, 0f, 0f), expectedShootCount: 1);
            AssertShootOffers(shooter, target, new float3(PISTOL_MAXIMUM_RANGE + 2f, 0f, 0f), expectedShootCount: 0);
            AssertShootOffers(shooter, target, new float3(PISTOL_MINIMUM_RANGE - 2f, 0f, 0f), expectedShootCount: 0);

            entityManager.SetComponentData(shooter, new TacticsCooldown { keepRangeReadyAtSeconds = 0.0, rangedReadyAtSeconds = 12.0 });
            AssertShootOffers(shooter, target, new float3(8f, 0f, 0f), expectedShootCount: 0);
        }

        [Test]
        public void LineOfFire_DisciplinedShooterSideStepsInsteadOfShootingThroughAnAlly()
        {
            DynamicBuffer<UtilityActions> actions = RunLineOfFireCase(resourcefulness: 0.8f, out Entity shooter);

            Assert.AreEqual(0, CountActionsOfType(actions, ActionType.Shoot));
            Assert.AreEqual(1, CountActionsOfType(actions, ActionType.KeepRange));
            UtilityActions sideStep = FindAction(actions, ActionType.KeepRange);
            Assert.IsTrue(sideStep.hasTargetPosition);
            Assert.AreEqual(RangedAttackAwarenessSystem.SIDE_STEP_METERS, math.abs(sideStep.targetPosition.z), 0.01f);
            Assert.AreEqual(0f, sideStep.targetPosition.x, 0.01f);
        }

        [Test]
        public void LineOfFire_CarelessShooterShootsThroughAnAlly()
        {
            DynamicBuffer<UtilityActions> actions = RunLineOfFireCase(resourcefulness: 0.1f, out Entity shooter);

            Assert.AreEqual(1, CountActionsOfType(actions, ActionType.Shoot));
            Assert.AreEqual(0, CountActionsOfType(actions, ActionType.KeepRange));
        }

        [Test]
        public void AiShoot_SpawnsBulletTowardAimPointThatDamagesAndDespawnsOnHit()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity bulletPrefab = entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform), typeof(Projectile), typeof(Lifetime), typeof(Despawn));
            entityManager.SetComponentData(bulletPrefab, new Projectile { radius = 0.05f });
            entityManager.SetComponentEnabled<Despawn>(bulletPrefab, false);
            Entity itemLibraryEntity = CreateItemLibrarySingleton(PistolBlob());
            DynamicBuffer<ItemProjectilePrefab> projectilePrefabs = entityManager.AddBuffer<ItemProjectilePrefab>(itemLibraryEntity);
            for (int itemIndex = 0; itemIndex < Enum.GetValues(typeof(ItemType)).Length; itemIndex++)
                projectilePrefabs.Add(new ItemProjectilePrefab { prefab = itemIndex == (int)ItemType.Pistol ? bulletPrefab : Entity.Null });
            CreateAttackLibrarySingleton();
            CreateDamageBusSingleton();

            Entity victim = entityManager.CreateEntity(typeof(LocalTransform), typeof(Health));
            entityManager.SetComponentData(victim, LocalTransform.FromPosition(new float3(0f, 0f, 4f)));
            Entity shooter = CreateGunman(Entity.Null);
            entityManager.AddComponent<AiShootRequest>(shooter);
            entityManager.SetComponentData(shooter, new AiShootRequest { target = victim, aimPoint = new float3(0f, 0f, 10f), hasAimPoint = true });

            RunSystem<AiShootSystem>();

            Entity bullet = SingleLiveBullet();
            Projectile projectile = entityManager.GetComponentData<Projectile>(bullet);
            Assert.AreEqual(shooter, projectile.shooter);
            Assert.AreEqual(DamageSource.ShootOneHand, projectile.damageSource);
            Assert.Greater(math.dot(math.normalize(projectile.velocity.xz), new float2(0f, 1f)), 0.999f);
            Assert.AreEqual(PISTOL_SPEED, math.length(projectile.velocity), 0.01f);
            Assert.AreEqual(1.5f * PISTOL_MAXIMUM_RANGE / PISTOL_SPEED, entityManager.GetComponentData<Lifetime>(bullet).secondsRemaining, 0.001f);
            Assert.AreEqual(10.0 + PISTOL_COOLDOWN_SECONDS, entityManager.GetComponentData<TacticsCooldown>(shooter).rangedReadyAtSeconds, 0.001);

            for (int frame = 0; frame < 10 && !entityManager.IsComponentEnabled<Despawn>(bullet); frame++)
                RunSystem<ProjectileFlightSystem>();

            Assert.IsTrue(entityManager.IsComponentEnabled<Despawn>(bullet));
            Assert.AreEqual(1, damageBus.raw.Count);
            DamageEvent damageEvent = damageBus.raw.Dequeue();
            Assert.AreEqual(victim, damageEvent.targetEntity);
            Assert.AreEqual(shooter, damageEvent.sourceEntity);
            Assert.AreEqual(PISTOL_DAMAGE, damageEvent.damageAmount);
        }

        [Test]
        public void WeaponGrab_PrefersRifleOverNearerRock()
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateBrainLibrarySingleton(ActionType.EquipWeapon);
            CreateItemLibrarySingleton(
                new ItemBlob { itemType = ItemType.Rock, category = ItemCategory.Weapon, aiWeaponValue = 1f },
                new ItemBlob { itemType = ItemType.Rifle, category = ItemCategory.Weapon, aiWeaponValue = 4f });
            CreateEmptyEffectLibrarySingleton();

            NativeParallelMultiHashMap<Entity, Entity> occupancy = new NativeParallelMultiHashMap<Entity, Entity>(4, Allocator.Persistent);
            persistentResources.Add(occupancy);
            entityManager.AddComponentData(entityManager.CreateEntity(), new TargetOccupancy { occupantsByTarget = occupancy });
            entityManager.AddComponentData(entityManager.CreateEntity(), new LoggingConfig { EnabledCategories = 0 });

            NativeParallelMultiHashMap<int2, Entity> itemCells = new NativeParallelMultiHashMap<int2, Entity>(4, Allocator.Persistent);
            persistentResources.Add(itemCells);
            Entity rock = CreateLooseItem(ItemType.Rock, new float3(1.5f, 0f, 0f), itemCells);
            Entity rifle = CreateLooseItem(ItemType.Rifle, new float3(4f, 0f, 0f), itemCells);
            entityManager.AddComponentData(entityManager.CreateEntity(), new SpatialHashRegistry { itemCells = itemCells });

            Entity attacker = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(attacker, LocalTransform.FromPosition(new float3(0f, 0f, 15f)));
            Entity unit = CreateBanditUnit(float3.zero, typeof(Awareness), typeof(Health), typeof(ThreatEntry),
                typeof(PersonalityAttributes), typeof(UnitEquip));
            entityManager.SetComponentData(unit, new Awareness { range = 20f });
            entityManager.SetComponentData(unit, new Health { healthAmount = 100, healthAmountMax = 100 });
            entityManager.SetComponentData(unit, new UnitEquip { equipItemEntity = Entity.Null, socketEntity = Entity.Null });
            entityManager.GetBuffer<ThreatEntry>(unit).Add(new ThreatEntry { attackerEntity = attacker, threatScore = 10f, staleTimer = 5f });

            RunSystem<ItemAwarenessSystem>();

            DynamicBuffer<UtilityActions> actions = entityManager.GetBuffer<UtilityActions>(unit);
            Assert.AreEqual(1, CountActionsOfType(actions, ActionType.EquipWeapon));
            Assert.AreEqual(rifle, FindAction(actions, ActionType.EquipWeapon).targetEntity);
            Assert.AreNotEqual(rock, FindAction(actions, ActionType.EquipWeapon).targetEntity);
        }

        private DynamicBuffer<UtilityActions> RunLineOfFireCase(float resourcefulness, out Entity shooter)
        {
            EntityManager entityManager = testWorld.EntityManager;
            CreateBrainLibrarySingleton(ActionType.Shoot, ActionType.KeepRange);
            CreateItemLibrarySingleton(PistolBlob());

            Entity target = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(target, LocalTransform.FromPosition(new float3(10f, 0f, 0f)));
            shooter = CreateGunman(target);
            entityManager.AddBuffer<PersonalityAttributes>(shooter).Add(new PersonalityAttributes
            {
                personality = PersonalityTypes.Resourceful,
                value = resourcefulness,
            });

            Entity allyInTheWay = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(allyInTheWay, LocalTransform.FromPosition(new float3(5f, 0f, 0.3f)));
            NativeParallelMultiHashMap<byte, Entity> factionEntities = new NativeParallelMultiHashMap<byte, Entity>(4, Allocator.Persistent);
            persistentResources.Add(factionEntities);
            factionEntities.Add((byte)FactionType.Bandits, shooter);
            factionEntities.Add((byte)FactionType.Bandits, allyInTheWay);
            entityManager.AddComponentData(entityManager.CreateEntity(), new FactionRegistry { entities = factionEntities });

            RunSystem<RangedAttackAwarenessSystem>();
            return entityManager.GetBuffer<UtilityActions>(shooter);
        }

        private void AssertShootOffers(Entity shooter, Entity target, float3 targetPosition, int expectedShootCount)
        {
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.SetComponentData(target, LocalTransform.FromPosition(targetPosition));
            entityManager.GetBuffer<UtilityActions>(shooter).Clear();

            RunSystem<RangedAttackAwarenessSystem>();

            DynamicBuffer<UtilityActions> actions = entityManager.GetBuffer<UtilityActions>(shooter);
            Assert.AreEqual(expectedShootCount, CountActionsOfType(actions, ActionType.Shoot), "target at " + targetPosition);
            if (expectedShootCount > 0)
            {
                UtilityActions shoot = FindAction(actions, ActionType.Shoot);
                Assert.AreEqual(target, shoot.targetEntity);
                Assert.AreEqual(AIPriorityTier.Critical, shoot.priority);
            }
        }

        private static ItemBlob PistolBlob()
        {
            return new ItemBlob
            {
                itemType              = ItemType.Pistol,
                category              = ItemCategory.Weapon,
                weaponAttack          = DamageSource.ShootOneHand,
                rangedKind            = RangedKind.Shoot,
                projectileSpeed       = PISTOL_SPEED,
                rangedCooldownSeconds = PISTOL_COOLDOWN_SECONDS,
                aiRangedMinRange      = PISTOL_MINIMUM_RANGE,
                aiRangedMaxRange      = PISTOL_MAXIMUM_RANGE,
                aiWeaponValue         = 3f,
            };
        }

        private Entity CreateGunman(Entity target)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity pistol = entityManager.CreateEntity(typeof(Item));
            entityManager.SetComponentData(pistol, new Item { itemType = ItemType.Pistol });

            Entity shooter = CreateBanditUnit(float3.zero, typeof(UnitEquip), typeof(TacticsCooldown));
            entityManager.SetComponentData(shooter, new UnitEquip { equipItemEntity = pistol, socketEntity = Entity.Null });
            entityManager.SetComponentData(shooter, new StateMachine { action = ActionType.MeleeSingle, targetEntity = target });
            return shooter;
        }

        private Entity CreateLooseItem(ItemType itemType, float3 position, NativeParallelMultiHashMap<int2, Entity> itemCells)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity item = entityManager.CreateEntity(typeof(Item), typeof(EquipBy), typeof(LocalTransform));
            entityManager.SetComponentData(item, new Item { itemType = itemType });
            entityManager.SetComponentData(item, new EquipBy { owner = Entity.Null });
            entityManager.SetComponentData(item, LocalTransform.FromPosition(position));
            itemCells.Add(InteractionSpatialHashSystem.GetCell(position), item);
            return item;
        }

        private Entity SingleLiveBullet()
        {
            EntityQuery bulletQuery = testWorld.EntityManager.CreateEntityQuery(typeof(Projectile));
            NativeArray<Entity> bullets = bulletQuery.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, bullets.Length);
            Entity bullet = bullets[0];
            bullets.Dispose();
            return bullet;
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<TSystem>();
            handle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
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

        private Entity CreateItemLibrarySingleton(params ItemBlob[] itemBlobs)
        {
            EntityManager entityManager = testWorld.EntityManager;
            int itemTypeCount = Enum.GetValues(typeof(ItemType)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref ItemLibraryBlob root = ref builder.ConstructRoot<ItemLibraryBlob>();
            BlobBuilderArray<ItemBlob> items = builder.Allocate(ref root.items, itemTypeCount);
            foreach (ItemBlob itemBlob in itemBlobs)
                items[(int)itemBlob.itemType] = itemBlob;
            BlobAssetReference<ItemLibraryBlob> itemLibrary = builder.CreateBlobAssetReference<ItemLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            persistentResources.Add(itemLibrary);
            Entity itemLibraryEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(itemLibraryEntity, new ItemLibrary { library = itemLibrary });
            return itemLibraryEntity;
        }

        private void CreateAttackLibrarySingleton()
        {
            EntityManager entityManager = testWorld.EntityManager;
            int damageSourceCount = Enum.GetValues(typeof(DamageSource)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref AttackLibraryBlob root = ref builder.ConstructRoot<AttackLibraryBlob>();
            BlobBuilderArray<AttackBlob> attacks = builder.Allocate(ref root.attacks, damageSourceCount);
            attacks[(int)DamageSource.ShootOneHand] = new AttackBlob { damageSource = DamageSource.ShootOneHand, damageAmount = PISTOL_DAMAGE };
            BlobAssetReference<AttackLibraryBlob> attackLibrary = builder.CreateBlobAssetReference<AttackLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            persistentResources.Add(attackLibrary);
            entityManager.AddComponentData(entityManager.CreateEntity(), new AttackLibrary { library = attackLibrary });
        }

        private void CreateEmptyEffectLibrarySingleton()
        {
            EntityManager entityManager = testWorld.EntityManager;
            int effectTypeCount = Enum.GetValues(typeof(EffectType)).Length;
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref EffectLibraryBlob root = ref builder.ConstructRoot<EffectLibraryBlob>();
            BlobBuilderArray<EffectBlob> effects = builder.Allocate(ref root.effects, effectTypeCount);
            for (int index = 0; index < effectTypeCount; index++)
                builder.Allocate(ref effects[index].behaviours, 0);
            BlobAssetReference<EffectLibraryBlob> effectLibrary = builder.CreateBlobAssetReference<EffectLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            persistentResources.Add(effectLibrary);
            entityManager.AddComponentData(entityManager.CreateEntity(), new EffectLibrary { library = effectLibrary });
        }

        private void CreateDamageBusSingleton()
        {
            damageBus = new DamageBus
            {
                raw      = new NativeQueue<DamageEvent>(Allocator.Persistent),
                resolved = new NativeQueue<DamageEvent>(Allocator.Persistent),
            };
            testWorld.EntityManager.AddComponentData(testWorld.EntityManager.CreateEntity(), damageBus);
        }
    }
}
