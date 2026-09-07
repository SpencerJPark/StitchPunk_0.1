// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// <see cref="DirectionSlotsBlob.ResolveSlot"/> is the per-entry fold: the caller's facing has
    /// already been quantized at the actor's own direction count, and this folds it again into what
    /// THIS entry actually authored. Without it a Two-coverage entry on a Six-turning actor returns
    /// an empty <see cref="ClipId"/> for every rear facing, which reads on screen as the actor
    /// freezing whenever it faces away from the camera — the exact failure this pins.
    /// </summary>
    public sealed class ActorProfileFoldTests
    {
        private static readonly ClipId SouthEastClip = new ClipId(11UL);
        private static readonly ClipId NorthEastClip = new ClipId(22UL);
        private static readonly ClipId SouthClip = new ClipId(33UL);

        private static DirectionSlotsBlob TwoCoverageSlots()
        {
            return new DirectionSlotsBlob
            {
                southEast = SouthEastClip,
                effectiveDirections = AnimationDirections.Two,
            };
        }

        [Test]
        public void TwoCoverageSet_FoldsARearFacingOntoItsFrontThreeQuarter()
        {
            DirectionSlotsBlob directionSlots = TwoCoverageSlots();

            Assert.AreEqual(SouthEastClip, directionSlots.ResolveSlot(Direction.NorthEast));
        }

        [Test]
        public void TwoCoverageSet_FoldsHeadOnOntoItsFrontThreeQuarter()
        {
            DirectionSlotsBlob directionSlots = TwoCoverageSlots();

            Assert.AreEqual(SouthEastClip, directionSlots.ResolveSlot(Direction.South));
        }

        [Test]
        public void OneCoverageSet_PlaysItsSouthClipForEveryFacing()
        {
            DirectionSlotsBlob directionSlots = new DirectionSlotsBlob
            {
                south = SouthClip,
                effectiveDirections = AnimationDirections.One,
            };

            Assert.AreEqual(SouthClip, directionSlots.ResolveSlot(Direction.SouthEast));
            Assert.AreEqual(SouthClip, directionSlots.ResolveSlot(Direction.North));
            Assert.AreEqual(SouthClip, directionSlots.ResolveSlot(Direction.East));
        }

        [Test]
        public void FullCoverageSet_KeepsTheFacingItWasAskedFor()
        {
            // The fold must be a no-op when the entry covers what the actor turns through, or a
            // six-direction character would quietly lose its rear art.
            DirectionSlotsBlob directionSlots = new DirectionSlotsBlob
            {
                southEast = SouthEastClip,
                northEast = NorthEastClip,
                south = SouthClip,
                north = new ClipId(44UL),
                effectiveDirections = AnimationDirections.Six,
            };

            Assert.AreEqual(NorthEastClip, directionSlots.ResolveSlot(Direction.NorthEast));
            Assert.AreEqual(SouthClip, directionSlots.ResolveSlot(Direction.South));
        }
    }
}
