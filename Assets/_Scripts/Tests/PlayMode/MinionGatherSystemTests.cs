using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class MinionGatherSystemTests
    {
        private World testWorld;
        private BlobAssetReference<BrainLibraryBlob> brainLibraryBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("MinionGatherSystemTests");
            brainLibraryBlob = BuildBrainLibrary();
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.AddComponentData(entityManager.CreateEntity(), new BrainLibrary { blob = brainLibraryBlob });
            // Creating the ECB system registers the singleton the gather system asks for.
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            testWorld.SetTime(new TimeData(10.0, 3.5f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (brainLibraryBlob.IsCreated) brainLibraryBlob.Dispose();
        }

        [Test]
        public void Harvesting_PastHarvestSeconds_CarriesOneLoadAndHeadsToStation()
        {
            Entity player = CreatePlayer();
            Entity station = CreateStation(player, float3.zero);
            Entity tree = CreateNode(ResourceType.Wood, new float3(20f, 0f, 0f), 2);
            Entity minion = CreateMinion(new float3(20.5f, 0f, 0f), station, tree, GatherPhase.Harvesting);

            RunGatherSystem();

            MinionGatherOrder order = testWorld.EntityManager.GetComponentData<MinionGatherOrder>(minion);
            Assert.AreEqual(1, order.carriedAmount);
            Assert.AreEqual(GatherPhase.ToStation, order.phase);
            Assert.AreEqual(1, testWorld.EntityManager.GetComponentData<ResourceNode>(tree).remaining);
        }

        [Test]
        public void ToStation_AtDropPoint_DepositsLoadAndReturnsToNode()
        {
            Entity player = CreatePlayer();
            Entity station = CreateStation(player, float3.zero);
            Entity tree = CreateNode(ResourceType.Wood, new float3(20f, 0f, 0f), 2);
            Entity minion = CreateMinion(new float3(0.5f, 0f, 0f), station, tree, GatherPhase.ToStation);
            MinionGatherOrder seeded = testWorld.EntityManager.GetComponentData<MinionGatherOrder>(minion);
            seeded.carriedAmount = 1;
            testWorld.EntityManager.SetComponentData(minion, seeded);

            RunGatherSystem();

            DynamicBuffer<ResourceChangeRequest> requests = testWorld.EntityManager.GetBuffer<ResourceChangeRequest>(player);
            Assert.AreEqual(1, requests.Length);
            Assert.AreEqual(ResourceType.Wood, requests[0].resourceType);
            Assert.AreEqual(ResourceChangeType.Add, requests[0].changeType);
            Assert.AreEqual(1, requests[0].value);
            MinionGatherOrder order = testWorld.EntityManager.GetComponentData<MinionGatherOrder>(minion);
            Assert.AreEqual(0, order.carriedAmount);
            Assert.AreEqual(GatherPhase.ToNode, order.phase);
        }

        [Test]
        public void ToNode_NodeEmptied_RetargetsToNearbyNodeOfSameType()
        {
            Entity player = CreatePlayer();
            Entity station = CreateStation(player, float3.zero);
            Entity emptiedTree = CreateNode(ResourceType.Wood, new float3(20f, 0f, 0f), 0);
            Entity nextTree = CreateNode(ResourceType.Wood, new float3(25f, 0f, 0f), 3);
            Entity minion = CreateMinion(new float3(10f, 0f, 0f), station, emptiedTree, GatherPhase.ToNode);

            RunGatherSystem();

            MinionGatherOrder order = testWorld.EntityManager.GetComponentData<MinionGatherOrder>(minion);
            Assert.AreEqual(nextTree, order.node);
            Assert.AreEqual(GatherPhase.ToNode, order.phase);
            DynamicBuffer<UtilityActions> actions = testWorld.EntityManager.GetBuffer<UtilityActions>(minion);
            Assert.AreEqual(1, actions.Length);
            Assert.Less(math.distance(new float3(25f, 0f, 0f), actions[0].targetPosition), 0.001f);
        }

        [Test]
        public void PhaseNone_AddsNoOptions()
        {
            Entity player = CreatePlayer();
            Entity station = CreateStation(player, float3.zero);
            Entity tree = CreateNode(ResourceType.Wood, new float3(20f, 0f, 0f), 2);
            Entity minion = CreateMinion(new float3(10f, 0f, 0f), station, tree, GatherPhase.None);

            RunGatherSystem();

            Assert.AreEqual(0, testWorld.EntityManager.GetBuffer<UtilityActions>(minion).Length);
        }

        private void RunGatherSystem()
        {
            SystemHandle gatherHandle = testWorld.GetOrCreateSystem<MinionGatherSystem>();
            gatherHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private Entity CreatePlayer()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = entityManager.CreateEntity(typeof(Player), typeof(LocalTransform), typeof(ResourceChangeRequest));
            entityManager.SetComponentData(player, LocalTransform.Identity);
            return player;
        }

        private Entity CreateStation(Entity player, float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity station = entityManager.CreateEntity(typeof(Workstation), typeof(LocalTransform));
            entityManager.SetComponentData(station, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(station, new Workstation { ownerPlayer = player });
            return station;
        }

        private Entity CreateNode(ResourceType resourceType, float3 position, int remaining)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity node = entityManager.CreateEntity(typeof(ResourceNode), typeof(LocalTransform));
            entityManager.SetComponentData(node, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(node, new ResourceNode
            {
                resourceType = resourceType,
                remaining = remaining,
                harvestSeconds = 3f,
                loadPerTrip = 1,
            });
            return node;
        }

        private Entity CreateMinion(float3 position, Entity station, Entity node, GatherPhase phase)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity minion = entityManager.CreateEntity(
                typeof(PlayerUnitBrain), typeof(UtilityBrain), typeof(Dead), typeof(MinionGatherOrder),
                typeof(LocalTransform), typeof(UnitData), typeof(UtilityActions));
            entityManager.SetComponentEnabled<UtilityBrain>(minion, false);
            entityManager.SetComponentEnabled<Dead>(minion, false);
            entityManager.SetComponentData(minion, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(minion, new UnitData { unitType = UnitType.MaleCitizen });
            entityManager.SetComponentData(minion, new MinionGatherOrder
            {
                phase = phase,
                node = node,
                resourceType = ResourceType.Wood,
                station = station,
                lastIssuedDestination = new float3(1000f, 0f, 1000f),
            });
            if (phase == GatherPhase.ToNode && entityManager.HasComponent<ResourceNode>(node))
            {
                MinionGatherOrder order = entityManager.GetComponentData<MinionGatherOrder>(minion);
                order.lastIssuedDestination = entityManager.GetComponentData<LocalTransform>(node).Position;
                entityManager.SetComponentData(minion, order);
            }
            return minion;
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
                        builder.Allocate(ref brainsBuilder[brainIndex].actionDefIndices, 1);
                    actionDefIndices[0] = 0;
                }
                else
                {
                    builder.Allocate(ref brainsBuilder[brainIndex].actionDefIndices, 0);
                }
            }

            BlobBuilderArray<ActionDefBlob> actionDefsBuilder = builder.Allocate(ref root.actionDefs, 1);
            actionDefsBuilder[0].actionType = ActionType.Wander;
            builder.Allocate(ref actionDefsBuilder[0].considerations, 0);

            BlobAssetReference<BrainLibraryBlob> blob =
                builder.CreateBlobAssetReference<BrainLibraryBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }
    }
}
