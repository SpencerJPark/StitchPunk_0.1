using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // First PlayMode World fixture over the interpreter (BehaviorCommandSplit_System.md §10) — the
    // split into Utils/BehaviorCommands/*.cs is what makes this writable at all. Pins command-index
    // progression through the Execute -> Complete phase machine for a scripted 3-command behavior;
    // not a coverage sweep of every BehaviorCommandType arm.
    public sealed class BehaviorExecutionSystemTests
    {
        private const BehaviorType TestBehaviorType = BehaviorType.Wander;

        private World testWorld;
        private BlobAssetReference<BehaviorLibraryBlob> behaviorLibraryBlob;
        private BlobAssetReference<UnitLibraryBlob> unitLibraryBlob;
        private NativeParallelMultiHashMap<int2, Entity> waypointCells;
        private Entity unit;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("BehaviorExecutionSystemTests");
            EntityManager entityManager = testWorld.EntityManager;

            // Creates the EndSimulationEntityCommandBufferSystem.Singleton the job reads to build its ECB.
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

            behaviorLibraryBlob = BuildBehaviorLibraryWithThreeCommandSequence();
            unitLibraryBlob = BuildEmptyUnitLibrary();
            waypointCells = new NativeParallelMultiHashMap<int2, Entity>(1, Allocator.Persistent);

            Entity configEntity = entityManager.CreateEntity();
            entityManager.AddComponent<GameSceneTag>(configEntity);
            entityManager.AddComponentData(configEntity, new BehaviorLibrary { blob = behaviorLibraryBlob });
            entityManager.AddComponentData(configEntity, new UnitDataLibrary { library = unitLibraryBlob });
            entityManager.AddComponentData(configEntity, new SpatialHashRegistry { waypointCells = waypointCells });

            unit = entityManager.CreateEntity();
            entityManager.AddComponentData(unit, LocalTransform.Identity);
            entityManager.AddComponentData(unit, new StateMachine
            {
                action         = ActionType.Wander,
                activeBehavior = TestBehaviorType,
                currentPhase   = BehaviorPhase.Execute,
            });
            entityManager.AddComponentData(unit, new PathRequest());
            entityManager.AddBuffer<RecentWaypoint>(unit);
            entityManager.AddBuffer<RecentInteraction>(unit);
            entityManager.AddBuffer<AvailableAttack>(unit);
            entityManager.AddBuffer<MotivationChangeRequest>(unit);
            entityManager.AddComponentData(unit, new UtilityBrain());
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            if (behaviorLibraryBlob.IsCreated) behaviorLibraryBlob.Dispose();
            if (unitLibraryBlob.IsCreated) unitLibraryBlob.Dispose();
            if (waypointCells.IsCreated) waypointCells.Dispose();
        }

        [Test]
        public void ThreeCommandBehavior_AdvancesOneCommandPerTick_ThenCompletes()
        {
            Assert.AreEqual(0, GetStateMachine().CurrentCommandIndex, "Guard: starts at command 0.");

            RunBehaviorExecutionSystem();
            Assert.AreEqual(1, GetStateMachine().CurrentCommandIndex,
                "The first ModifyMotivation is fire-and-advance — one tick must move to command 1.");
            Assert.AreEqual(BehaviorPhase.Execute, GetStateMachine().currentPhase);

            RunBehaviorExecutionSystem();
            Assert.AreEqual(2, GetStateMachine().CurrentCommandIndex);

            RunBehaviorExecutionSystem();
            Assert.AreEqual(3, GetStateMachine().CurrentCommandIndex,
                "The third command advances the index past the end of the sequence.");
            Assert.AreEqual(BehaviorPhase.Execute, GetStateMachine().currentPhase,
                "The phase only flips to Complete once RunExecute sees CurrentCommandIndex >= length, " +
                "on the NEXT tick — not the tick that produced the out-of-range index.");

            RunBehaviorExecutionSystem();
            Assert.AreEqual(BehaviorPhase.Complete, GetStateMachine().currentPhase,
                "CurrentCommandIndex >= executionSequence.Length must flip the phase to Complete.");

            RunBehaviorExecutionSystem();
            StateMachine resetStateMachine = GetStateMachine();
            Assert.AreEqual(BehaviorType.None, resetStateMachine.activeBehavior,
                "The Complete phase resets the unit to Idle on the tick after it is entered.");
            Assert.AreEqual(0, resetStateMachine.CurrentCommandIndex);
        }

        private StateMachine GetStateMachine()
        {
            return testWorld.EntityManager.GetComponentData<StateMachine>(unit);
        }

        private void RunBehaviorExecutionSystem()
        {
            SystemHandle executionSystem = testWorld.GetOrCreateSystem<BehaviorExecutionSystem>();
            executionSystem.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        // One behavior (TestBehaviorType) with a 3-step ModifyMotivation sequence — the simplest
        // fire-and-advance command, chosen so this fixture pins index progression through the split
        // interpreter's dispatch without dragging in movement/animation setup.
        private static BlobAssetReference<BehaviorLibraryBlob> BuildBehaviorLibraryWithThreeCommandSequence()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref BehaviorLibraryBlob root = ref builder.ConstructRoot<BehaviorLibraryBlob>();
            BlobBuilderArray<BehaviorConfigBlob> behaviorsBuilder =
                builder.Allocate(ref root.behaviors, (int)TestBehaviorType + 1);

            for (int behaviorIndex = 0; behaviorIndex <= (int)TestBehaviorType; behaviorIndex++)
            {
                behaviorsBuilder[behaviorIndex].behaviorType = (BehaviorType)behaviorIndex;
                builder.Allocate(ref behaviorsBuilder[behaviorIndex].interruptionCleanup, 0);

                if (behaviorIndex != (int)TestBehaviorType)
                {
                    builder.Allocate(ref behaviorsBuilder[behaviorIndex].executionSequence, 0);
                    continue;
                }

                BlobBuilderArray<BehaviorCommand> executionSequence =
                    builder.Allocate(ref behaviorsBuilder[behaviorIndex].executionSequence, 3);
                executionSequence[0] = new BehaviorCommand
                {
                    type       = BehaviorCommandType.ModifyMotivation,
                    IntParam   = (int)NeedType.Hunger,
                    FloatParam = 1f,
                };
                executionSequence[1] = new BehaviorCommand
                {
                    type       = BehaviorCommandType.ModifyMotivation,
                    IntParam   = (int)NeedType.Energy,
                    FloatParam = 2f,
                };
                executionSequence[2] = new BehaviorCommand
                {
                    type       = BehaviorCommandType.ModifyMotivation,
                    IntParam   = (int)NeedType.Fun,
                    FloatParam = 3f,
                };
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
