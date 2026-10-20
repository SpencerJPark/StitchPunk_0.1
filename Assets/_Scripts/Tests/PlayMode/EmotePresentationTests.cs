using DotsAnimationToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class EmotePresentationTests
    {
        private World testWorld;
        private SystemHandle emotePresentationSystem;
        private NativeList<Entity> slotEntities;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("EmotePresentationTests");
            testWorld.SetTime(new TimeData(10.0, 0.05f));
            emotePresentationSystem = testWorld.GetOrCreateSystem<EmotePresentationSystem>();
            slotEntities = new NativeList<Entity>(32, Allocator.Persistent);

            Entity cameraEntity = testWorld.EntityManager.CreateEntity(typeof(AnimationToolkitCameraData));
            testWorld.EntityManager.SetComponentData(cameraEntity, new AnimationToolkitCameraData
            {
                position = float3.zero,
                forward = new float3(0f, 0f, 1f)
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (slotEntities.IsCreated) slotEntities.Dispose();
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void OnlyBudgetSlotsShowWhenCrowded()
        {
            CreatePool(24);
            for (int unitIndex = 0; unitIndex < 40; unitIndex++)
            {
                float depth = 5f + unitIndex * (15f / 39f);
                CreateEmotingUnit(new float3(0f, 0f, depth), 1);
            }

            RunPresentation(2);

            int expectedBudget = CrowdLifeBudget.Budget(40, 24);
            Assert.AreEqual(10, expectedBudget);
            Assert.AreEqual(expectedBudget, CountOwnedSlots());
            CrowdLifeLod crowdLifeLod = testWorld.EntityManager
                .CreateEntityQuery(typeof(CrowdLifeLod)).GetSingleton<CrowdLifeLod>();
            Assert.AreEqual(40, crowdLifeLod.visibleHumans);
            Assert.AreEqual(expectedBudget, crowdLifeLod.emoteBudget);
        }

        [Test]
        public void HigherPriorityAndNearerWinSlots()
        {
            CreatePool(2);
            CreateEmotingUnit(new float3(0f, 0f, 5f), 1);
            CreateEmotingUnit(new float3(0f, 0f, 6f), 1);
            Entity farHighPriority = CreateEmotingUnit(new float3(0f, 0f, 30f), 3);
            Entity midMediumPriority = CreateEmotingUnit(new float3(0f, 0f, 15f), 2);

            RunPresentation(1);

            NativeList<Entity> owners = new NativeList<Entity>(2, Allocator.Temp);
            for (int slotIndex = 0; slotIndex < slotEntities.Length; slotIndex++)
            {
                owners.Add(testWorld.EntityManager.GetComponentData<EmotePoolSlot>(slotEntities[slotIndex]).owner);
            }
            Assert.IsTrue(owners.Contains(farHighPriority), "priority 3 unit must own a slot");
            Assert.IsTrue(owners.Contains(midMediumPriority), "priority 2 unit must own a slot");
            owners.Dispose();
        }

        [Test]
        public void BehindCameraAndFarUnitsGetNoSlot()
        {
            CreatePool(4);
            CreateEmotingUnit(new float3(0f, 0f, -5f), 1);
            CreateEmotingUnit(new float3(0f, 0f, 50f), 1);

            RunPresentation(1);

            Assert.AreEqual(0, CountOwnedSlots());
        }

        [Test]
        public void HiddenSlotsAreScaleZeroAlphaZero()
        {
            CreatePool(4);
            CreateEmotingUnit(new float3(0f, 0f, 8f), 1);

            RunPresentation(1);

            int hiddenCount = 0;
            for (int slotIndex = 0; slotIndex < slotEntities.Length; slotIndex++)
            {
                Entity slotEntity = slotEntities[slotIndex];
                if (testWorld.EntityManager.GetComponentData<EmotePoolSlot>(slotEntity).owner != Entity.Null)
                {
                    continue;
                }
                hiddenCount++;
                Assert.AreEqual(0f, testWorld.EntityManager.GetComponentData<LocalTransform>(slotEntity).Scale);
                Assert.AreEqual(0f, testWorld.EntityManager.GetComponentData<URPMaterialPropertyBaseColor>(slotEntity).Value.w);
            }
            Assert.AreEqual(3, hiddenCount);
        }

        private void CreatePool(int poolSize)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity configEntity = entityManager.CreateEntity(typeof(EmotePoolConfig));
            entityManager.SetComponentData(configEntity, new EmotePoolConfig
            {
                poolSize = poolSize,
                viewDistance = 35f,
                headHeight = 1.8f,
                firstIconMaterialIndex = 0
            });

            for (int slotIndex = 0; slotIndex < poolSize; slotIndex++)
            {
                Entity slotEntity = entityManager.CreateEntity(
                    typeof(LocalTransform),
                    typeof(MaterialMeshInfo),
                    typeof(URPMaterialPropertyBaseColor),
                    typeof(EmotePoolSlot));
                entityManager.SetComponentData(slotEntity, new LocalTransform { Position = float3.zero, Rotation = quaternion.identity, Scale = 0f });
                entityManager.SetComponentData(slotEntity, MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
                entityManager.SetComponentData(slotEntity, new URPMaterialPropertyBaseColor { Value = new float4(1f, 1f, 1f, 0f) });
                entityManager.SetComponentData(slotEntity, new EmotePoolSlot { slotIndex = slotIndex, shownFor = 0f, owner = Entity.Null });
                slotEntities.Add(slotEntity);
            }
        }

        private Entity CreateEmotingUnit(float3 position, byte priority)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(typeof(EmoteState), typeof(LocalToWorld));
            entityManager.SetComponentData(unit, new EmoteState
            {
                icon = EmoteIcon.Happy,
                remaining = 5f,
                duration = 5f,
                priority = priority
            });
            entityManager.SetComponentData(unit, new LocalToWorld { Value = float4x4.Translate(position) });
            return unit;
        }

        private void RunPresentation(int updateCount)
        {
            for (int updateIndex = 0; updateIndex < updateCount; updateIndex++)
            {
                emotePresentationSystem.Update(testWorld.Unmanaged);
                testWorld.EntityManager.CompleteAllTrackedJobs();
            }
        }

        private int CountOwnedSlots()
        {
            int ownedCount = 0;
            for (int slotIndex = 0; slotIndex < slotEntities.Length; slotIndex++)
            {
                if (testWorld.EntityManager.GetComponentData<EmotePoolSlot>(slotEntities[slotIndex]).owner != Entity.Null)
                {
                    ownedCount++;
                }
            }
            return ownedCount;
        }
    }
}
