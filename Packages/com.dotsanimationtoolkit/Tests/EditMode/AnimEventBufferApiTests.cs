// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Covers TryFindNextEvent's scan-and-advance contract and the ContainsEvent/TryFindEvent wrappers built on it.</summary>
    public sealed class AnimEventBufferApiTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("AnimEventBufferApiTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;
        }

        [Test]
        public void TryFindNextEvent_VisitsEverySameKeyEventInOrder()
        {
            Entity entity = testWorld.EntityManager.CreateEntity();
            DynamicBuffer<AnimEventOutput> events = testWorld.EntityManager.AddBuffer<AnimEventOutput>(entity);
            events.Add(new AnimEventOutput { eventKey = 20u, intParam = 1 });
            events.Add(new AnimEventOutput { eventKey = 21u, intParam = 2 });
            events.Add(new AnimEventOutput { eventKey = 20u, intParam = 3 });

            List<int> visited = new List<int>();
            int searchIndex = 0;
            int iterationGuard = 0;
            while (AnimEventBufferApi.TryFindNextEvent(events, 20u, ref searchIndex, out AnimEventOutput found))
            {
                visited.Add(found.intParam);
                iterationGuard++;
                if (iterationGuard >= 10)
                {
                    Assert.Fail("TryFindNextEvent did not advance searchIndex past its match.");
                }
            }

            CollectionAssert.AreEqual(new[] { 1, 3 }, visited);

            int missingSearchIndex = 0;
            int missingIterationGuard = 0;
            while (AnimEventBufferApi.TryFindNextEvent(events, 22u, ref missingSearchIndex, out AnimEventOutput missingFound))
            {
                missingIterationGuard++;
                if (missingIterationGuard >= 10)
                {
                    Assert.Fail("TryFindNextEvent did not advance searchIndex past its match.");
                }
            }

            Assert.IsTrue(AnimEventBufferApi.ContainsEvent(events, 21u));
            Assert.IsFalse(AnimEventBufferApi.ContainsEvent(events, 22u));
            Assert.IsTrue(AnimEventBufferApi.TryFindEvent(events, 20u, out AnimEventOutput first));
            Assert.AreEqual(1, first.intParam);
        }
    }
}
