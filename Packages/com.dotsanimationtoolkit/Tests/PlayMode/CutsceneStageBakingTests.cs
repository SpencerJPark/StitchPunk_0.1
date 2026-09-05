// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.PlayMode
{
    /// <summary>
    /// Covers <c>CutsceneStageBaker</c> (amendment A61-T2): a <c>CutsceneStageAuthoring</c> component
    /// must bake one <c>CutsceneStage</c> entity carrying the cutscene's blob, plus a
    /// <c>CutsceneStageBinding</c> per resolvable slot binding — and skip an unconfigured stage or an
    /// unresolvable slot id rather than bake something broken.
    /// </summary>
    public sealed class CutsceneStageBakingTests
    {
        private BakingTestWorld bakingWorld;
        private CutsceneAsset cutscene;
        private GameObject stageGameObject;
        private GameObject targetGameObject;

        [SetUp]
        public void SetUp()
        {
            bakingWorld = new BakingTestWorld("CutsceneStageBakingTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Failed)
            {
                bakingWorld.AssertNoUnexpectedToolkitErrors();
            }
            bakingWorld.Dispose();

            if (stageGameObject != null)
            {
                Object.DestroyImmediate(stageGameObject);
            }
            if (targetGameObject != null)
            {
                Object.DestroyImmediate(targetGameObject);
            }
            if (cutscene != null)
            {
                Object.DestroyImmediate(cutscene);
            }
        }

        [Test]
        public void CutsceneStageAuthoring_BakesStageEntityWithBinding()
        {
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            CutsceneSlot propSlot = new CutsceneSlot { name = "Cart", kind = CutsceneSlotKind.Prop };
            cutscene.slots.Add(propSlot);
            cutscene.EnsureStableIds();
            uint slotId = propSlot.SlotId;

            targetGameObject = new GameObject("Target");

            stageGameObject = new GameObject("Stage");
            CutsceneStageAuthoring stageAuthoring = stageGameObject.AddComponent<CutsceneStageAuthoring>();
            stageAuthoring.cutscene = cutscene;
            stageAuthoring.bindings.Add(new CutsceneStageSlotBinding { slotId = slotId, target = targetGameObject });

            bakingWorld.Bake(targetGameObject, stageGameObject);

            Entity stageEntity = bakingWorld.GetPrimaryEntity(stageGameObject);
            EntityManager entityManager = bakingWorld.EntityManager;

            Assert.IsTrue(entityManager.HasComponent<CutsceneStage>(stageEntity), "CutsceneStage.");
            CutsceneStage stage = entityManager.GetComponentData<CutsceneStage>(stageEntity);
            Assert.IsTrue(stage.blob.IsCreated, "the blob must be created and owned by the BlobAssetStore");
            Assert.AreEqual(cutscene.StableId, stage.cutsceneKey);

            Assert.IsTrue(entityManager.HasBuffer<CutsceneStageBinding>(stageEntity), "CutsceneStageBinding buffer.");
            DynamicBuffer<CutsceneStageBinding> stageBindings =
                entityManager.GetBuffer<CutsceneStageBinding>(stageEntity);
            Assert.AreEqual(1, stageBindings.Length);
            Assert.AreEqual(slotId, stageBindings[0].slotId);
            Assert.AreEqual(bakingWorld.GetPrimaryEntity(targetGameObject), stageBindings[0].target);
        }

        [Test]
        public void CutsceneStageAuthoring_UnresolvedSlotId_SkippedWithOneWarning()
        {
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            cutscene.EnsureStableIds();

            targetGameObject = new GameObject("Target");

            stageGameObject = new GameObject("Stage");
            CutsceneStageAuthoring stageAuthoring = stageGameObject.AddComponent<CutsceneStageAuthoring>();
            stageAuthoring.cutscene = cutscene;
            stageAuthoring.bindings.Add(new CutsceneStageSlotBinding { slotId = 999u, target = targetGameObject });

            bakingWorld.Bake(targetGameObject, stageGameObject);

            Entity stageEntity = bakingWorld.GetPrimaryEntity(stageGameObject);
            DynamicBuffer<CutsceneStageBinding> stageBindings =
                bakingWorld.EntityManager.GetBuffer<CutsceneStageBinding>(stageEntity);
            Assert.AreEqual(0, stageBindings.Length, "an unresolvable slot id must be skipped, not baked");

            IReadOnlyList<string> warnings = bakingWorld.ToolkitWarnings;
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("does not declare", warnings[0]);
        }

        [Test]
        public void CutsceneStageAuthoring_NoCutsceneAssigned_BakesNothing()
        {
            stageGameObject = new GameObject("Stage");
            stageGameObject.AddComponent<CutsceneStageAuthoring>();

            bakingWorld.Bake(stageGameObject);

            Entity stageEntity = bakingWorld.GetPrimaryEntity(stageGameObject);
            Assert.IsFalse(bakingWorld.EntityManager.HasComponent<CutsceneStage>(stageEntity));
        }
    }
}
