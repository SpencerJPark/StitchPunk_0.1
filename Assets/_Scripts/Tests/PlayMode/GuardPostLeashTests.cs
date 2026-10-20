using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class GuardPostLeashTests
    {
        private const float IDLE_RADIUS = 10f;
        private const float LEASH_RADIUS = 25f;
        private const float GUARD_DISTANCE_FROM_ANCHOR = 22f;

        private World testWorld;
        private BlobAssetReference<BrainLibraryBlob> brainLibraryBlob;
        private Entity guard;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("GuardPostLeashTests");
            testWorld.SetTime(new TimeData(10.0, 0.05f));
            EntityManager entityManager = testWorld.EntityManager;

            int brainCount = (int)UnitType.Bandit + 1;
            BlobBuilder blobBuilder = new BlobBuilder(Allocator.Temp);
            ref BrainLibraryBlob root = ref blobBuilder.ConstructRoot<BrainLibraryBlob>();
            BlobBuilderArray<BrainBlob> brains = blobBuilder.Allocate(ref root.brains, brainCount);
            for (int index = 0; index < brainCount; index++)
                blobBuilder.Allocate(ref brains[index].actionDefIndices, 0);
            BlobBuilderArray<int> banditDefIndices = blobBuilder.Allocate(ref brains[(int)UnitType.Bandit].actionDefIndices, 1);
            banditDefIndices[0] = 0;
            BlobBuilderArray<ActionDefBlob> actionDefs = blobBuilder.Allocate(ref root.actionDefs, 1);
            actionDefs[0].actionType = ActionType.Wander;
            actionDefs[0].behavior = BehaviorType.Wander;
            blobBuilder.Allocate(ref actionDefs[0].considerations, 0);
            brainLibraryBlob = blobBuilder.CreateBlobAssetReference<BrainLibraryBlob>(Allocator.Persistent);
            blobBuilder.Dispose();

            Entity libraryEntity = entityManager.CreateEntity(typeof(BrainLibrary));
            entityManager.SetComponentData(libraryEntity, new BrainLibrary { blob = brainLibraryBlob });

            guard = entityManager.CreateEntity(
                typeof(GuardPost), typeof(UtilityBrain), typeof(Dead), typeof(LocalTransform),
                typeof(StateMachine), typeof(ActionInterruptRequest));
            entityManager.AddBuffer<UtilityActions>(guard);
            entityManager.SetComponentEnabled<Dead>(guard, false);
            entityManager.SetComponentEnabled<ActionInterruptRequest>(guard, false);
            entityManager.SetComponentData(guard, new GuardPost
            {
                anchorPosition = float3.zero,
                idleRadius = IDLE_RADIUS,
                leashRadius = LEASH_RADIUS,
                keepFightingWithinMeters = 6f,
            });
            entityManager.SetComponentData(guard, new UtilityBrain { unitType = UnitType.Bandit });
            entityManager.SetComponentData(guard, LocalTransform.FromPosition(new float3(GUARD_DISTANCE_FROM_ANCHOR, 0f, 0f)));
            entityManager.SetComponentData(guard, new StateMachine
            {
                action = ActionType.Wander,
                activeBehavior = BehaviorType.Wander,
                hasTargetPosition = true,
                targetPosition = float3.zero,
                activePriority = AIPriorityTier.Leash,
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (brainLibraryBlob.IsCreated) brainLibraryBlob.Dispose();
        }

        [Test]
        public void ReturningGuardTurnsBackForHostileInsideTheLeash()
        {
            EntityManager entityManager = testWorld.EntityManager;
            AddHostileOption(15f);

            RunGuardPostAwareness();

            DynamicBuffer<UtilityActions> actions = entityManager.GetBuffer<UtilityActions>(guard);
            Assert.IsTrue(ContainsOption(actions, ActionType.MeleeContinuous), "combat option must survive");
            for (int index = 0; index < actions.Length; index++)
                Assert.AreNotEqual(AIPriorityTier.Leash, actions[index].priority, "no leash-return option expected");
            Assert.IsTrue(entityManager.IsComponentEnabled<ActionInterruptRequest>(guard));
        }

        [Test]
        public void ReturningGuardIgnoresHostileOnTheLeashEdge()
        {
            EntityManager entityManager = testWorld.EntityManager;
            AddHostileOption(-23f);

            RunGuardPostAwareness();

            DynamicBuffer<UtilityActions> actions = entityManager.GetBuffer<UtilityActions>(guard);
            Assert.IsFalse(ContainsOption(actions, ActionType.MeleeContinuous), "combat option must be dropped");
            bool foundLeashReturn = false;
            for (int index = 0; index < actions.Length; index++)
            {
                UtilityActions option = actions[index];
                if (option.actionType != ActionType.Wander || option.priority != AIPriorityTier.Leash) continue;
                Assert.IsTrue(option.hasTargetPosition);
                Assert.AreEqual(0f, math.distance(option.targetPosition, float3.zero), 0.001f);
                foundLeashReturn = true;
            }
            Assert.IsTrue(foundLeashReturn, "a Wander option toward the anchor at Leash priority is expected");
            Assert.IsFalse(entityManager.IsComponentEnabled<ActionInterruptRequest>(guard));
        }

        [Test]
        public void ReturningGuardKeepsFightingHostileRightNextToItEvenOutsideTheLeash()
        {
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.SetComponentData(guard, LocalTransform.FromPosition(new float3(28f, 0f, 0f)));
            AddHostileOption(30f);

            RunGuardPostAwareness();

            DynamicBuffer<UtilityActions> actions = entityManager.GetBuffer<UtilityActions>(guard);
            Assert.IsTrue(ContainsOption(actions, ActionType.MeleeContinuous), "combat option must survive");
            for (int index = 0; index < actions.Length; index++)
                Assert.AreNotEqual(AIPriorityTier.Leash, actions[index].priority, "no leash-return option expected");
            Assert.IsTrue(entityManager.IsComponentEnabled<ActionInterruptRequest>(guard));
        }

        [Test]
        public void GuardPastTheLeashGivesUpOnAFarHostile()
        {
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.SetComponentData(guard, LocalTransform.FromPosition(new float3(28f, 0f, 0f)));
            entityManager.SetComponentData(guard, new StateMachine
            {
                action = ActionType.MeleeContinuous,
                activeBehavior = BehaviorType.MeleeContinuous,
                activePriority = 3,
            });
            AddHostileOption(40f);

            RunGuardPostAwareness();

            DynamicBuffer<UtilityActions> actions = entityManager.GetBuffer<UtilityActions>(guard);
            Assert.IsFalse(ContainsOption(actions, ActionType.MeleeContinuous), "combat option must be dropped");
            bool foundLeashReturn = false;
            for (int index = 0; index < actions.Length; index++)
            {
                UtilityActions option = actions[index];
                if (option.actionType != ActionType.Wander || option.priority != AIPriorityTier.Leash) continue;
                Assert.IsTrue(option.hasTargetPosition);
                Assert.AreEqual(0f, math.distance(option.targetPosition, float3.zero), 0.001f);
                foundLeashReturn = true;
            }
            Assert.IsTrue(foundLeashReturn, "a Wander option toward the anchor at Leash priority is expected");
        }

        private void AddHostileOption(float distanceFromAnchor)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity hostile = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(hostile, LocalTransform.FromPosition(new float3(distanceFromAnchor, 0f, 0f)));
            entityManager.GetBuffer<UtilityActions>(guard).Add(new UtilityActions
            {
                actionType = ActionType.MeleeContinuous,
                targetEntity = hostile,
                actionDefIndex = 0,
                priority = 3,
            });
        }

        private static bool ContainsOption(DynamicBuffer<UtilityActions> actions, ActionType actionType)
        {
            for (int index = 0; index < actions.Length; index++)
            {
                if (actions[index].actionType == actionType) return true;
            }
            return false;
        }

        private void RunGuardPostAwareness()
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<GuardPostAwarenessSystem>();
            handle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
