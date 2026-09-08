using System.Collections.Generic;
using System.Diagnostics;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;

namespace StitchPunk.Tests.PlayMode
{
    // G3-P2: guards against an accidental per-frame allocation or O(n^2) walk creeping into
    // CutsceneTimelineSystem, using the real acceptance cutscene's blob rather than a synthetic one.
    // The bound entities are bare LocalTransform stand-ins, not real actors — the system under test
    // only reads/writes transform + bookkeeping components, never anything actor-specific.
    public sealed class CutsceneAcceptancePerfTests
    {
        private const string CutsceneAssetPath = "Assets/ScriptableObjects/Cutscenes/RendezvousAndDepart.asset";
        private const int FrameCount = 600;
        private const float DeltaTime = 1f / 60f;

        // Generous on purpose (HANDOFF §2): the point is catching an accidental allocation or
        // O(n^2) walk, not pinning an exact number to a test machine. A healthy run measures
        // comfortably under 2 ms; this bound is 25x that so CI/dev-machine variance never flakes it.
        private const double GenerousTotalMillisecondsBound = 50.0;

        private World testWorld;
        private BlobAssetReference<CutsceneBlob> cutsceneBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneAcceptancePerfTests");
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
        public void CutsceneTimelineSystem_SixHundredFrames_OfTheAcceptanceCutscene_StaysWellUnderBudget()
        {
            CutsceneAsset cutscene = AssetDatabase.LoadAssetAtPath<CutsceneAsset>(CutsceneAssetPath);
            Assert.IsNotNull(cutscene, "RendezvousAndDepart.asset must exist for this perf test (G3 §2).");

            List<string> warnings = new List<string>();
            CutsceneBlobBuilder.Build(cutscene, out cutsceneBlob, warnings);

            EntityManager entityManager = testWorld.EntityManager;
            Entity requestEntity = CutsceneApi.CreatePlayRequest(entityManager, cutsceneBlob, layerIndex: CutsceneApi.TopLayer, speed: 1f);

            // Every bound entity created before the buffer is fetched: CreateEntity/AddComponent are
            // structural changes that invalidate an already-fetched DynamicBuffer handle.
            Entity[] boundEntities = new Entity[cutscene.slots.Count];
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                boundEntities[slotIndex] = CreateBoundStandIn(entityManager);
            }

            DynamicBuffer<CutsceneActorBinding> actorBindings = entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                actorBindings.Add(new CutsceneActorBinding
                {
                    slotId = cutscene.slots[slotIndex].SlotId,
                    actorEntity = boundEntities[slotIndex],
                });
            }

            SystemHandle timelineSystem = testWorld.GetOrCreateSystem<CutsceneTimelineSystem>();

            // One warm-up update outside the timer: first call pays one-time job compilation /
            // system-state allocation cost that every subsequent frame does not.
            testWorld.SetTime(new Unity.Core.TimeData(0d, DeltaTime));
            timelineSystem.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            Stopwatch stopwatch = Stopwatch.StartNew();
            double elapsedSeconds = DeltaTime;
            for (int frameIndex = 0; frameIndex < FrameCount; frameIndex++)
            {
                testWorld.SetTime(new Unity.Core.TimeData(elapsedSeconds, DeltaTime));
                timelineSystem.Update(testWorld.Unmanaged);
                elapsedSeconds += DeltaTime;
            }
            entityManager.CompleteAllTrackedJobs();
            stopwatch.Stop();

            UnityEngine.Debug.Log(
                "CutsceneAcceptancePerfTests: " + FrameCount + " frames of CutsceneTimelineSystem.Update took "
                + stopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms total.");

            Assert.Less(stopwatch.Elapsed.TotalMilliseconds, GenerousTotalMillisecondsBound,
                FrameCount + " frames of CutsceneTimelineSystem.Update took "
                + stopwatch.Elapsed.TotalMilliseconds.ToString("F3")
                + " ms - past the generous budget, suggesting a per-frame allocation or O(n^2) walk.");
        }

        private static Entity CreateBoundStandIn(EntityManager entityManager)
        {
            Entity entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, new LocalTransform
            {
                Position = float3.zero,
                Rotation = quaternion.identity,
                Scale = 1f,
            });
            entityManager.AddComponentData(entity, new PostTransformMatrix { Value = float4x4.identity });
            entityManager.AddBuffer<PlaybackLayer>(entity);
            return entity;
        }
    }
}
