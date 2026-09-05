using DotsAnimationToolkit;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // G1-P2: manual World + GetOrCreateSystem<T>().Update(...) fixtures over the two new
    // CutsceneSystemGroup systems — no scene/GameObjects, mirrors BehaviorExecutionSystemTests.
    public sealed class CutsceneSystemTests
    {
        private const ulong TestCutsceneKey = 42UL;

        private World testWorld;
        private BlobAssetReference<CutsceneBlob> cutsceneBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneSystemTests");
            EntityManager entityManager = testWorld.EntityManager;

            Entity configEntity = entityManager.CreateEntity();
            entityManager.AddComponent<GameSceneTag>(configEntity);
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;

            if (cutsceneBlob.IsCreated) cutsceneBlob.Dispose();
        }

        [Test]
        public void CutsceneStartSystem_BindsTheStage_EnablesCutsceneActor_AndRaisesInterrupt()
        {
            EntityManager entityManager = testWorld.EntityManager;

            Entity narrativeEntity = entityManager.CreateEntity();
            entityManager.AddComponent<NarrativeEventTag>(narrativeEntity);
            entityManager.AddComponent<ActiveCutscene>(narrativeEntity);
            entityManager.SetComponentEnabled<ActiveCutscene>(narrativeEntity, false);

            Entity unitEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(unitEntity, new LocalTransform
            {
                Position = new float3(5f, 0f, 5f),
                Rotation = quaternion.identity,
                Scale    = 1f,
            });
            entityManager.AddComponentData(unitEntity, new Movement { targetPosition = new float3(99f, 0f, 99f) });
            entityManager.AddComponentData(unitEntity, new PathRequest { requestedMode = PathfindingMode.DStarLite });
            entityManager.SetComponentEnabled<PathRequest>(unitEntity, false);
            entityManager.AddComponent<CutsceneActor>(unitEntity);
            entityManager.SetComponentEnabled<CutsceneActor>(unitEntity, false);
            entityManager.AddComponent<ActionInterruptRequest>(unitEntity);
            entityManager.SetComponentEnabled<ActionInterruptRequest>(unitEntity, false);

            cutsceneBlob = BuildEmptyCutsceneBlob();
            Entity stageEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(stageEntity, new CutsceneStage
            {
                blob        = cutsceneBlob,
                cutsceneKey = TestCutsceneKey,
            });
            DynamicBuffer<CutsceneStageBinding> stageBindings = entityManager.AddBuffer<CutsceneStageBinding>(stageEntity);
            stageBindings.Add(new CutsceneStageBinding { slotId = 0, target = unitEntity });

            Entity signalEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(signalEntity, new CutsceneRequest
            {
                cutsceneKey = TestCutsceneKey,
                layerIndex  = 2,
                speed       = 1f,
            });

            RunSystem<CutsceneStartSystem>();

            Assert.IsFalse(entityManager.Exists(signalEntity), "The signal entity must be destroyed the same frame it is consumed.");
            Assert.IsTrue(entityManager.IsComponentEnabled<CutsceneActor>(unitEntity),
                "CutsceneStartSystem must enable CutsceneActor on every bound actor.");
            Assert.IsTrue(entityManager.IsComponentEnabled<ActionInterruptRequest>(unitEntity),
                "CutsceneStartSystem must raise the single AI teardown path on every bound actor.");
            Assert.AreEqual(PathfindingMode.Stop, entityManager.GetComponentData<PathRequest>(unitEntity).requestedMode,
                "MovementAPI.HaltPathing must stop any in-flight path.");
            Assert.IsTrue(entityManager.IsComponentEnabled<PathRequest>(unitEntity));
            Assert.AreEqual(new float3(5f, 0f, 5f), entityManager.GetComponentData<Movement>(unitEntity).targetPosition,
                "Movement.targetPosition must snap to the actor's current position so it stops immediately.");

            Assert.IsTrue(entityManager.IsComponentEnabled<ActiveCutscene>(narrativeEntity));
            Entity playRequestEntity = entityManager.GetComponentData<ActiveCutscene>(narrativeEntity).playRequest;
            Assert.IsTrue(entityManager.Exists(playRequestEntity));
            Assert.IsTrue(entityManager.HasComponent<CutscenePlaybackState>(playRequestEntity));
        }

        [Test]
        public void CutsceneEndSystem_ReleasesActorsAndDestroysTheRequest()
        {
            EntityManager entityManager = testWorld.EntityManager;

            Entity unitEntity = entityManager.CreateEntity();
            entityManager.AddComponent<CutsceneActor>(unitEntity);
            entityManager.SetComponentEnabled<CutsceneActor>(unitEntity, true);
            entityManager.AddComponent<ActionInterruptRequest>(unitEntity);
            entityManager.SetComponentEnabled<ActionInterruptRequest>(unitEntity, false);
            entityManager.AddComponent<ActionRequest>(unitEntity);
            entityManager.SetComponentEnabled<ActionRequest>(unitEntity, false);

            Entity playRequestEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(playRequestEntity, new CutscenePlaybackState { isComplete = true });
            DynamicBuffer<CutsceneActorBinding> actorBindings = entityManager.AddBuffer<CutsceneActorBinding>(playRequestEntity);
            actorBindings.Add(new CutsceneActorBinding { slotId = 0, actorEntity = unitEntity });

            Entity narrativeEntity = entityManager.CreateEntity();
            entityManager.AddComponent<NarrativeEventTag>(narrativeEntity);
            entityManager.AddComponentData(narrativeEntity, new ActiveCutscene { playRequest = playRequestEntity });
            entityManager.SetComponentEnabled<ActiveCutscene>(narrativeEntity, true);

            RunSystem<CutsceneEndSystem>();

            Assert.IsFalse(entityManager.IsComponentEnabled<CutsceneActor>(unitEntity),
                "CutsceneEndSystem must release every bound actor back to AI control.");
            Assert.IsTrue(entityManager.IsComponentEnabled<ActionInterruptRequest>(unitEntity),
                "CutsceneEndSystem must re-arm the brain through the single interrupt path.");
            Assert.IsTrue(entityManager.IsComponentEnabled<ActionRequest>(unitEntity),
                "CutsceneEndSystem must trigger an immediate awareness pass rather than waiting for the next decay tick.");
            Assert.IsFalse(entityManager.Exists(playRequestEntity),
                "The toolkit leaves destroying the completed request to the host.");
            Assert.IsFalse(entityManager.IsComponentEnabled<ActiveCutscene>(narrativeEntity));
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<TSystem>();
            systemHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        // Zero slots, zero segments — CreatePlayRequestFromStage only reads blob.slots.Length to
        // size its internal bookkeeping; the timeline sampler (unexercised by these fixtures)
        // is what would care about segments.
        private static BlobAssetReference<CutsceneBlob> BuildEmptyCutsceneBlob()
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = 1;
                root.cutsceneKey   = TestCutsceneKey;
                builder.Allocate(ref root.slots, 0);
                builder.Allocate(ref root.segments, 0);
                return builder.CreateBlobAssetReference<CutsceneBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }
        }
    }
}
