using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class TargetReservationSystemTests
    {
        private World testWorld;
        private BlobAssetReference<InteractionLibraryBlob> interactionLibraryBlob;
        private NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget;
        private Entity chair;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TargetReservationSystemTests");
            testWorld.SetTime(new TimeData(10.0, 0.05f));
            EntityManager entityManager = testWorld.EntityManager;

            // The system logs through this singleton's ECB; it is never played back here.
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            ref InteractionLibraryBlob root = ref blobBuilder.ConstructRoot<InteractionLibraryBlob>();
            BlobBuilderArray<InteractionBlob> interactions =
                blobBuilder.Allocate(ref root.interactions, (int)ActionType.Sit + 1 + (int)ActionType.Talk);
            for (int index = 0; index < interactions.Length; index++)
                blobBuilder.Allocate(ref interactions[index].allowedFactions, 0);
            interactions[(int)ActionType.Sit].maxOccupants = 1;
            interactions[(int)ActionType.Talk].maxOccupants = 1;
            interactionLibraryBlob = blobBuilder.CreateBlobAssetReference<InteractionLibraryBlob>(Allocator.Persistent);
            blobBuilder.Dispose();

            Entity libraryEntity = entityManager.CreateEntity(typeof(InteractionLibrary));
            entityManager.SetComponentData(libraryEntity, new InteractionLibrary { library = interactionLibraryBlob });

            occupantsByTarget = new NativeParallelMultiHashMap<Entity, Entity>(16, Allocator.Persistent);
            Entity occupancyEntity = entityManager.CreateEntity(typeof(TargetOccupancy));
            entityManager.SetComponentData(occupancyEntity, new TargetOccupancy { occupantsByTarget = occupantsByTarget });

            chair = entityManager.CreateEntity(typeof(Interaction));
            entityManager.SetComponentData(chair, new Interaction { action = ActionType.Sit });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (occupantsByTarget.IsCreated) occupantsByTarget.Dispose();
            if (interactionLibraryBlob.IsCreated) interactionLibraryBlob.Dispose();
        }

        [Test]
        public void SecondNewClaimOnSingleSeatChairIsRevertedAndForcedToRethink()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity firstUnit = CreateUnitSitting(chair, 1);
            Entity secondUnit = CreateUnitSitting(chair, 1);

            RunReservationSystem();

            bool firstKeeps = entityManager.GetComponentData<StateMachine>(firstUnit).targetEntity == chair;
            bool secondKeeps = entityManager.GetComponentData<StateMachine>(secondUnit).targetEntity == chair;
            Assert.AreNotEqual(firstKeeps, secondKeeps, "exactly one unit may keep the single-seat chair");

            Entity loser = firstKeeps ? secondUnit : firstUnit;
            StateMachine loserState = entityManager.GetComponentData<StateMachine>(loser);
            Assert.AreEqual(BehaviorType.None, loserState.activeBehavior);
            Assert.AreEqual(Entity.Null, loserState.targetEntity);
            Assert.AreEqual(0f, entityManager.GetComponentData<ThinkRate>(loser).timeUntilNextThink);
        }

        [Test]
        public void PlayerOrderedClaimIsAcceptedEvenWhenChairIsFull()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity occupantUnit = CreateUnitSitting(chair, 1);
            occupantsByTarget.Add(chair, occupantUnit);
            Entity orderedUnit = CreateUnitSitting(chair, int.MaxValue);

            RunReservationSystem();

            Assert.AreEqual(chair, entityManager.GetComponentData<StateMachine>(orderedUnit).targetEntity);
            Assert.AreEqual(BehaviorType.Sit, entityManager.GetComponentData<StateMachine>(orderedUnit).activeBehavior);
        }

        [Test]
        public void PendingClaimOnFullChairIsClearedButLiveBehaviourKept()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity occupantUnit = CreateUnitSitting(chair, 1);
            occupantsByTarget.Add(chair, occupantUnit);

            Entity wanderingUnit = CreateUnit(new StateMachine
            {
                activeBehavior = BehaviorType.Wander,
                targetEntity = Entity.Null,
                activePriority = 1,
                pendingAction = ActionType.Sit,
                pendingBehavior = BehaviorType.Sit,
                pendingTarget = chair,
                pendingPriority = 2,
            });

            RunReservationSystem();

            StateMachine state = entityManager.GetComponentData<StateMachine>(wanderingUnit);
            Assert.AreEqual(BehaviorType.None, state.pendingBehavior);
            Assert.AreEqual(BehaviorType.Wander, state.activeBehavior);
        }

        [Test]
        public void UnitsAttackingAUnitThatCarriesAnInteractionAreNeverReserved()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity talkingTarget = entityManager.CreateEntity(typeof(Interaction));
            entityManager.SetComponentData(talkingTarget, new Interaction { action = ActionType.Talk });

            Entity firstAttacker = CreateAttacker(talkingTarget);
            Entity secondAttacker = CreateAttacker(talkingTarget);

            RunReservationSystem();

            foreach (Entity attacker in new Entity[] { firstAttacker, secondAttacker })
            {
                Assert.AreEqual(talkingTarget, entityManager.GetComponentData<StateMachine>(attacker).targetEntity);
                Assert.AreNotEqual(0f, entityManager.GetComponentData<ThinkRate>(attacker).timeUntilNextThink);
            }
        }

        private Entity CreateAttacker(Entity target)
        {
            return CreateUnit(new StateMachine
            {
                action = ActionType.MeleeContinuous,
                activeBehavior = BehaviorType.MeleeContinuous,
                targetEntity = target,
                activePriority = 3,
            });
        }

        private Entity CreateUnitSitting(Entity target, int priority)
        {
            return CreateUnit(new StateMachine
            {
                action = ActionType.Sit,
                activeBehavior = BehaviorType.Sit,
                targetEntity = target,
                activePriority = priority,
            });
        }

        private Entity CreateUnit(StateMachine stateMachine)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(
                typeof(UtilityBrain), typeof(StateMachine), typeof(ThinkRate), typeof(Dead), typeof(CutsceneActor));
            entityManager.SetComponentEnabled<Dead>(unit, false);
            entityManager.SetComponentEnabled<CutsceneActor>(unit, false);
            entityManager.SetComponentData(unit, stateMachine);
            entityManager.SetComponentData(unit, new ThinkRate { interval = 0.25f, timeUntilNextThink = 1f });
            return unit;
        }

        private void RunReservationSystem()
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<TargetReservationSystem>();
            handle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
