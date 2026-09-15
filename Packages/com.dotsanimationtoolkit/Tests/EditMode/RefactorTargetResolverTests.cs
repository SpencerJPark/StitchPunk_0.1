// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>EditMode coverage of <see cref="RefactorTargetResolver"/>: which markers and tracks a refactor touches.</summary>
    public sealed class RefactorTargetResolverTests
    {
        private ClipAsset clip;

        [SetUp]
        public void SetUp()
        {
            clip = ScriptableObject.CreateInstance<ClipAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void RekeyTouchesOnlyMatchingMarkers()
        {
            clip.events.Add(new EventMarker { eventKey = 16u, normalizedTime = 0.1f });
            clip.events.Add(new EventMarker { eventKey = 17u, normalizedTime = 0.5f });
            clip.events.Add(new EventMarker { eventKey = 16u, normalizedTime = 0.9f });

            List<int> matchingMarkerIndices = RefactorTargetResolver.FindEventMarkerIndices(clip, 16u);

            CollectionAssert.AreEqual(new List<int> { 0, 2 }, matchingMarkerIndices, "only the two markers on key 16 are re-keyed.");
        }

        [Test]
        public void ReplaceTagLeavesUntaggedTracksAlone()
        {
            clip.transformTracks.Add(new TransformTrack { targetId = 5u, tagId = 0u });
            clip.spriteTracks.Add(new SpriteTrack { targetId = 6u, tagId = 0u });

            Assert.IsEmpty(RefactorTargetResolver.FindTransformTrackIndices(clip, 0u), "a transform track bound by target id is never moved, even when replacing tag 0.");
            Assert.IsEmpty(RefactorTargetResolver.FindSpriteTrackIndices(clip, 0u), "a sprite track bound by target id is never moved, even when replacing tag 0.");
        }
    }
}
