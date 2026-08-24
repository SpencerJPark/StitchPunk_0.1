// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// <c>TimelineGeometry.HitsKey</c>'s per-shape grab box (Phase D13, Task 2).
    /// </summary>
    /// <remarks>
    /// Event markers moved from a scaled-up diamond to a pentagon pin with a narrower drawn
    /// half-width, so <c>TrackLaneElement</c> now hands a lane's own grab-box radius to
    /// <c>HitsKey</c> instead of relying on the single <c>KeyHitRadius</c> constant every key kind
    /// used to share. This pins the arithmetic the caller relies on: a caller-supplied radius
    /// changes what counts as a hit, and the old no-radius overload still means exactly what it did
    /// before this phase, so every pose/sprite/bone key call site that never changed keeps its old
    /// behaviour untouched.
    /// </remarks>
    public sealed class TimelineGeometryHitTests
    {
        private const float LaneWidth = 400f;

        [Test]
        public void HitsKey_DefaultOverload_MatchesExplicitKeyHitRadius()
        {
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, zoom: 1f, panNormalized: 0f);
            float keyTime = 0.5f;
            float keyX = geometry.TimeToX(keyTime);

            // Just inside the shared constant both ways: the two-arg overload must be exactly the
            // three-arg one called with TimelineGeometry.KeyHitRadius, not an independent copy of
            // the same number that could drift from it.
            float justInside = keyX + TimelineGeometry.KeyHitRadius - 0.5f;
            Assert.AreEqual(
                geometry.HitsKey(justInside, keyTime, TimelineGeometry.KeyHitRadius),
                geometry.HitsKey(justInside, keyTime));
        }

        [Test]
        public void HitsKey_CustomRadius_WidensTheHitWindowPastTheDefault()
        {
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, zoom: 1f, panNormalized: 0f);
            float keyTime = 0.5f;
            float keyX = geometry.TimeToX(keyTime);

            // A point just past the default pose-key radius: a pentagon marker's wider grab box
            // must still catch it, but the shared default must not.
            float pointerX = keyX + TimelineGeometry.KeyHitRadius + 1f;

            Assert.IsFalse(
                geometry.HitsKey(pointerX, keyTime),
                "A pointer past the default radius must miss when no wider radius is supplied.");
            Assert.IsTrue(
                geometry.HitsKey(pointerX, keyTime, TimelineGeometry.KeyHitRadius + 2f),
                "The same pointer must hit once a wider radius is explicitly supplied — this is "
                    + "the whole mechanism TrackLaneElement uses to give the event lane's pentagon "
                    + "a different grab box than every other lane's diamond.");
        }

        [Test]
        public void HitsKey_CustomRadius_NarrowerThanDefault_MissesAPointTheDefaultWouldHit()
        {
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, zoom: 1f, panNormalized: 0f);
            float keyTime = 0.5f;
            float keyX = geometry.TimeToX(keyTime);
            float pointerX = keyX + TimelineGeometry.KeyHitRadius - 1f;

            Assert.IsTrue(geometry.HitsKey(pointerX, keyTime));
            Assert.IsFalse(
                geometry.HitsKey(pointerX, keyTime, 1f),
                "A narrow explicit radius must be honoured, not silently floored at the default.");
        }

        [Test]
        public void HitsKey_ExactlyAtRadius_CountsAsAHit()
        {
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, zoom: 1f, panNormalized: 0f);
            float keyTime = 0.5f;
            float keyX = geometry.TimeToX(keyTime);

            Assert.IsTrue(
                geometry.HitsKey(keyX + 4f, keyTime, 4f),
                "The comparison is <=, so a pointer exactly on the grab box's edge still hits — a "
                    + "marker you can see but only just barely fail to click is the exact failure "
                    + "mode the task called out.");
        }
    }
}
