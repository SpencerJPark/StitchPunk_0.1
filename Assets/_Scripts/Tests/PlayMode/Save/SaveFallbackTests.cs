using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace StitchPunk.Tests.PlayMode
{
    // SL-5-4: an old save whose component layout differs must never throw or take other components down with it.
    // Pinned policy: a size-mismatched component is skipped (live value kept) with a warning; the rest still applies.
    public sealed class SaveFallbackTests
    {
        private const double SavedSeconds = 1234.5;
        private const double LiveSeconds = 7.0;

        private World testWorld;
        private EntityManager entityManager;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SaveFallbackTests");
            entityManager = testWorld.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
                testWorld.Dispose();
            testWorld = null;
        }

        private EntityRecord WriteSourceRecord()
        {
            Entity source = entityManager.CreateEntity(typeof(LocalTransform), typeof(PlayTimeTracker));
            entityManager.SetComponentData(source, LocalTransform.FromPosition(new float3(5f, 6f, 7f)));
            entityManager.SetComponentData(source, new PlayTimeTracker { totalSeconds = SavedSeconds });
            return SaveSerialization.WriteEntity(entityManager, source, "Player");
        }

        private Entity CreateLiveTarget()
        {
            Entity target = entityManager.CreateEntity(typeof(LocalTransform), typeof(PlayTimeTracker));
            entityManager.SetComponentData(target, LocalTransform.FromPosition(float3.zero));
            entityManager.SetComponentData(target, new PlayTimeTracker { totalSeconds = LiveSeconds });
            return target;
        }

        private static ComponentRecord FindPlayTimeRecord(EntityRecord record)
        {
            foreach (ComponentRecord component in record.components)
            {
                if (component.type.Contains(nameof(PlayTimeTracker)))
                    return component;
            }
            Assert.Fail("PlayTimeTracker record was not written");
            return null;
        }

        private static void ResizePayload(ComponentRecord component, int newLength)
        {
            byte[] original = Convert.FromBase64String(component.data);
            byte[] resized = new byte[newLength];
            Array.Copy(original, resized, Math.Min(original.Length, newLength));
            component.data = Convert.ToBase64String(resized);
        }

        // SM-A1: an older save (component layout changed since) must skip only that component, never the record.
        [Test]
        public void Apply_TruncatedOldLayout_SkipsOnlyTheMismatchedComponent()
        {
            EntityRecord record = WriteSourceRecord();
            ResizePayload(FindPlayTimeRecord(record), sizeof(float));
            Entity target = CreateLiveTarget();

            LogAssert.Expect(LogType.Warning, new Regex("Size mismatch"));
            Assert.DoesNotThrow(() => SaveSerialization.ApplyEntity(entityManager, target, record));

            Assert.AreEqual(new float3(5f, 6f, 7f), entityManager.GetComponentData<LocalTransform>(target).Position);
            Assert.AreEqual(LiveSeconds, entityManager.GetComponentData<PlayTimeTracker>(target).totalSeconds);
        }
    }
}
