// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers the two directions a scrub can cross event markers: wrapping past the loop seam
    /// while playing, and a plain backward scrub while stopped.
    /// </summary>
    [TestFixture]
    public sealed class ScrubEventCrossingResolverTests
    {
        [Test]
        public void ForwardWrap_CrossesTailThenHead()
        {
            List<EventMarker> markers = new List<EventMarker>
            {
                new EventMarker { normalizedTime = 0.1f },
                new EventMarker { normalizedTime = 0.9f },
            };
            List<int> crossed = new List<int>();

            ScrubEventCrossingResolver.Resolve(0.85f, 0.15f, true, LoopMode.Loop, markers, crossed);

            CollectionAssert.AreEqual(new List<int> { 1, 0 }, crossed);
        }

        [Test]
        public void BackwardScrub_CrossesMarkerBetween()
        {
            List<EventMarker> markers = new List<EventMarker>
            {
                new EventMarker { normalizedTime = 0.5f },
            };
            List<int> crossed = new List<int>();

            ScrubEventCrossingResolver.Resolve(0.6f, 0.4f, false, LoopMode.Once, markers, crossed);

            CollectionAssert.AreEqual(new List<int> { 0 }, crossed);
        }
    }
}
