using System;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class MomentSnapshotTests
    {
        private const string StableTypeHashMarker = "\"stableTypeHash\":\"";

        private World liveWorld;
        private EntityManager entityManager;

        [SetUp]
        public void SetUp()
        {
            liveWorld = new World("MomentSnapshotTests");
            entityManager = liveWorld.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (liveWorld != null && liveWorld.IsCreated)
                liveWorld.Dispose();
            liveWorld = null;
        }

        private int CountMomentEntities()
        {
            EntityQuery momentQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MomentPersist>());
            int count = momentQuery.CalculateEntityCount();
            momentQuery.Dispose();
            return count;
        }

        private static byte[] CorruptFirstStableTypeHash(byte[] snapshotBytes)
        {
            byte[] corrupted = (byte[])snapshotBytes.Clone();
            int manifestLength = BitConverter.ToInt32(corrupted, 0);
            string manifestJson = Encoding.UTF8.GetString(corrupted, 4, manifestLength);
            int digitIndex = manifestJson.IndexOf(StableTypeHashMarker, StringComparison.Ordinal) + StableTypeHashMarker.Length;
            Assert.GreaterOrEqual(digitIndex, StableTypeHashMarker.Length, "manifest has no stableTypeHash entry");
            byte digitByte = corrupted[4 + digitIndex];
            corrupted[4 + digitIndex] = digitByte == (byte)'9' ? (byte)'8' : (byte)(digitByte + 1);
            return corrupted;
        }

        [Test]
        public void CommitRestoresCapturedStateAndTheSavedClock()
        {
            Entity unit = entityManager.CreateEntity(typeof(MomentPersist), typeof(LocalTransform), typeof(AttackRequest), typeof(TacticsCooldown));
            entityManager.SetComponentData(unit, LocalTransform.FromPosition(new float3(4f, 0f, -9f)));
            entityManager.SetComponentData(unit, new AttackRequest { hitFired = true, elapsed = 0.75f });
            entityManager.SetComponentData(unit, new TacticsCooldown { rangedReadyAtSeconds = 55.5, keepRangeReadyAtSeconds = 8.25 });

            byte[] snapshotBytes = MomentSnapshot.Capture(entityManager, out string layoutHash, out int referencedObjectCount);

            entityManager.SetComponentData(unit, LocalTransform.FromPosition(new float3(-1f, 2f, 3f)));
            entityManager.SetComponentData(unit, new AttackRequest { hitFired = false, elapsed = 9f });
            entityManager.SetComponentData(unit, new TacticsCooldown { rangedReadyAtSeconds = 1.0, keepRangeReadyAtSeconds = 2.0 });

            World stagingWorld = MomentSnapshot.DeserializeIntoStagingWorld(snapshotBytes);
            Assert.IsNotNull(stagingWorld);
            MomentSnapshot.Commit(entityManager, stagingWorld, 123.0);

            EntityQuery momentQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MomentPersist>());
            Assert.AreEqual(1, momentQuery.CalculateEntityCount());
            Entity restored = momentQuery.GetSingletonEntity();
            momentQuery.Dispose();

            Assert.AreEqual(new float3(4f, 0f, -9f), entityManager.GetComponentData<LocalTransform>(restored).Position);
            AttackRequest restoredAttack = entityManager.GetComponentData<AttackRequest>(restored);
            Assert.IsTrue(restoredAttack.hitFired);
            Assert.AreEqual(0.75f, restoredAttack.elapsed);
            TacticsCooldown restoredCooldown = entityManager.GetComponentData<TacticsCooldown>(restored);
            Assert.AreEqual(55.5, restoredCooldown.rangedReadyAtSeconds);
            Assert.AreEqual(8.25, restoredCooldown.keepRangeReadyAtSeconds);
            Assert.AreEqual(123.0, liveWorld.Time.ElapsedTime);
        }

        [Test]
        public void AlteredManifestTypeHashIsRejected()
        {
            entityManager.CreateEntity(typeof(MomentPersist), typeof(TacticsCooldown));
            byte[] snapshotBytes = MomentSnapshot.Capture(entityManager, out string layoutHash, out int referencedObjectCount);
            Assert.IsTrue(MomentSnapshot.LayoutMatches(snapshotBytes, out string untouchedReason), untouchedReason);

            byte[] corrupted = CorruptFirstStableTypeHash(snapshotBytes);

            Assert.IsFalse(MomentSnapshot.LayoutMatches(corrupted, out string mismatchReason));
            Assert.IsNotEmpty(mismatchReason);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[MomentSnapshot\]"));
            Assert.IsNull(MomentSnapshot.DeserializeIntoStagingWorld(corrupted));
        }

        [Test]
        public void LinkedChildSurvivesAndStaysInTheRootsLinkedEntityGroup()
        {
            Entity root = entityManager.CreateEntity(typeof(MomentPersist), typeof(LocalTransform), typeof(LinkedEntityGroup));
            Entity child = entityManager.CreateEntity(typeof(LocalTransform), typeof(Parent), typeof(AttackCooldown));
            entityManager.SetComponentData(child, new Parent { Value = root });
            entityManager.SetComponentData(child, new AttackCooldown { remaining = 7f });
            DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(root);
            group.Add(new LinkedEntityGroup { Value = root });
            group.Add(new LinkedEntityGroup { Value = child });

            byte[] snapshotBytes = MomentSnapshot.Capture(entityManager, out string layoutHash, out int referencedObjectCount);
            World stagingWorld = MomentSnapshot.DeserializeIntoStagingWorld(snapshotBytes);
            Assert.IsNotNull(stagingWorld);
            MomentSnapshot.Commit(entityManager, stagingWorld, 5.0);

            EntityQuery childQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AttackCooldown>());
            Assert.AreEqual(1, childQuery.CalculateEntityCount());
            // AttackCooldown is enableable, and GetSingletonEntity refuses queries over enableable types.
            Unity.Collections.NativeArray<Entity> restoredChildren = childQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            Entity restoredChild = restoredChildren[0];
            restoredChildren.Dispose();
            childQuery.Dispose();
            Assert.AreEqual(7f, entityManager.GetComponentData<AttackCooldown>(restoredChild).remaining);

            EntityQuery rootQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MomentPersist>());
            Entity restoredRoot = rootQuery.GetSingletonEntity();
            rootQuery.Dispose();
            Assert.AreEqual(restoredRoot, entityManager.GetComponentData<Parent>(restoredChild).Value);

            DynamicBuffer<LinkedEntityGroup> restoredGroup = entityManager.GetBuffer<LinkedEntityGroup>(restoredRoot);
            bool childLinked = false;
            for (int memberIndex = 0; memberIndex < restoredGroup.Length; memberIndex++)
            {
                if (restoredGroup[memberIndex].Value == restoredChild)
                    childLinked = true;
            }
            Assert.IsTrue(childLinked);
        }
    }
}
