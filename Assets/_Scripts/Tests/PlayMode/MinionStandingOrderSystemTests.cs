using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class MinionStandingOrderSystemTests
    {
        private const int FormationSlot = 3;
        private const float EngageRadius = 8f;

        private World testWorld;
        private BlobAssetReference<BrainLibraryBlob> brainLibraryBlob;
        private NativeParallelMultiHashMap<byte, Entity> factionEntities;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("MinionStandingOrderSystemTests");
            brainLibraryBlob = BuildBrainLibrary();
            factionEntities = new NativeParallelMultiHashMap<byte, Entity>(8, Allocator.Persistent);
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.AddComponentData(entityManager.CreateEntity(), new BrainLibrary { blob = brainLibraryBlob });
            entityManager.AddComponentData(entityManager.CreateEntity(), new FactionRegistry { entities = factionEntities });
            testWorld.SetTime(new TimeData(10.0, 0.016f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (factionEntities.IsCreated) factionEntities.Dispose();
            if (brainLibraryBlob.IsCreated) brainLibraryBlob.Dispose();
        }

        [Test]
        public void Follow_FarFromPlayer_IssuesOneWanderToFormationSlot()
        {
            float3 playerPosition = new float3(5f, 0f, 5f);
            CreatePlayer(playerPosition);
            Entity minion = CreateMinion(new float3(15f, 0f, 5f), StandingOrderType.FollowCommander);

            RunStandingOrderSystem();

            DynamicBuffer<UtilityActions> actions = testWorld.EntityManager.GetBuffer<UtilityActions>(minion);
            Assert.AreEqual(1, actions.Length);
            Assert.AreEqual(ActionType.Wander, actions[0].actionType);
            Assert.IsTrue(actions[0].hasTargetPosition);
            Assert.IsTrue(actions[0].isPlayerOrdered);
            float3 expected = playerPosition + FormationSlots.RoundedSlotOffset(FormationSlot, FormationSlots.DefaultSpacing);
            Assert.Less(math.distance(expected, actions[0].targetPosition), 0.001f);
        }

        [Test]
        public void Follow_SecondUpdateRightAfter_IsThrottled()
        {
            CreatePlayer(new float3(5f, 0f, 5f));
            Entity minion = CreateMinion(new float3(15f, 0f, 5f), StandingOrderType.FollowCommander);

            RunStandingOrderSystem();
            testWorld.EntityManager.GetBuffer<UtilityActions>(minion).Clear();
            RunStandingOrderSystem();

            Assert.AreEqual(0, testWorld.EntityManager.GetBuffer<UtilityActions>(minion).Length);
        }

        [Test]
        public void Follow_HostileNearPlayer_IssuesAttackOnHostile()
        {
            float3 playerPosition = new float3(5f, 0f, 5f);
            CreatePlayer(playerPosition);
            Entity minion = CreateMinion(new float3(15f, 0f, 5f), StandingOrderType.FollowCommander);
            Entity hostile = CreateHostile(playerPosition + new float3(3f, 0f, 0f));

            RunStandingOrderSystem();

            DynamicBuffer<UtilityActions> actions = testWorld.EntityManager.GetBuffer<UtilityActions>(minion);
            Assert.AreEqual(1, actions.Length);
            Assert.AreEqual(ActionType.MeleeSingle, actions[0].actionType);
            Assert.AreEqual(hostile, actions[0].targetEntity);
            Assert.IsTrue(actions[0].isPlayerOrdered);
        }

        [Test]
        public void NoStandingOrder_AddsNoOptions()
        {
            CreatePlayer(new float3(5f, 0f, 5f));
            Entity minion = CreateMinion(new float3(15f, 0f, 5f), StandingOrderType.None);
            CreateHostile(new float3(6f, 0f, 5f));

            RunStandingOrderSystem();

            Assert.AreEqual(0, testWorld.EntityManager.GetBuffer<UtilityActions>(minion).Length);
        }

        private void RunStandingOrderSystem()
        {
            SystemHandle standingOrderHandle = testWorld.GetOrCreateSystem<MinionStandingOrderSystem>();
            standingOrderHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private Entity CreatePlayer(float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = entityManager.CreateEntity(typeof(Player), typeof(LocalTransform));
            entityManager.SetComponentData(player, LocalTransform.FromPosition(position));
            return player;
        }

        private Entity CreateMinion(float3 position, StandingOrderType orderType)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity minion = entityManager.CreateEntity(
                typeof(PlayerUnitBrain), typeof(UtilityBrain), typeof(Dead), typeof(MinionStandingOrder),
                typeof(LocalTransform), typeof(StateMachine), typeof(UnitData),
                typeof(AttackFaction), typeof(AvailableAttack), typeof(UtilityActions));
            entityManager.SetComponentEnabled<UtilityBrain>(minion, false);
            entityManager.SetComponentEnabled<Dead>(minion, false);
            entityManager.SetComponentData(minion, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(minion, new UnitData { unitType = UnitType.MaleCitizen });
            entityManager.SetComponentData(minion, new MinionStandingOrder
            {
                type = orderType,
                formationSlotIndex = FormationSlot,
                engageRadius = EngageRadius,
                lastIssuedDestination = new float3(1000f, 0f, 1000f),
            });
            entityManager.GetBuffer<AttackFaction>(minion).Add(new AttackFaction { faction = FactionType.Citizens });
            entityManager.GetBuffer<AvailableAttack>(minion).Add(new AvailableAttack { actionType = ActionType.MeleeSingle });
            return minion;
        }

        private Entity CreateHostile(float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity hostile = entityManager.CreateEntity(typeof(LocalTransform), typeof(Dead));
            entityManager.SetComponentEnabled<Dead>(hostile, false);
            entityManager.SetComponentData(hostile, LocalTransform.FromPosition(position));
            factionEntities.Add((byte)FactionType.Citizens, hostile);
            return hostile;
        }

        private static BlobAssetReference<BrainLibraryBlob> BuildBrainLibrary()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref BrainLibraryBlob root = ref builder.ConstructRoot<BrainLibraryBlob>();

            int brainCount = BlobLibraryUtils.EnumCount<UnitType>();
            BlobBuilderArray<BrainBlob> brainsBuilder = builder.Allocate(ref root.brains, brainCount);
            for (int brainIndex = 0; brainIndex < brainCount; brainIndex++)
            {
                brainsBuilder[brainIndex].unitType = (UnitType)brainIndex;
                if ((UnitType)brainIndex == UnitType.MaleCitizen)
                {
                    BlobBuilderArray<int> actionDefIndices =
                        builder.Allocate(ref brainsBuilder[brainIndex].actionDefIndices, 2);
                    actionDefIndices[0] = 0;
                    actionDefIndices[1] = 1;
                }
                else
                {
                    builder.Allocate(ref brainsBuilder[brainIndex].actionDefIndices, 0);
                }
            }

            BlobBuilderArray<ActionDefBlob> actionDefsBuilder = builder.Allocate(ref root.actionDefs, 2);
            actionDefsBuilder[0].actionType = ActionType.Wander;
            actionDefsBuilder[1].actionType = ActionType.MeleeSingle;
            builder.Allocate(ref actionDefsBuilder[0].considerations, 0);
            builder.Allocate(ref actionDefsBuilder[1].considerations, 0);

            BlobAssetReference<BrainLibraryBlob> blob =
                builder.CreateBlobAssetReference<BrainLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }
    }
}
