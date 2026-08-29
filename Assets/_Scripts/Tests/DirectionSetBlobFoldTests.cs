using DotsAnimationToolkit;
using NUnit.Framework;

namespace StitchPunk.Tests
{
    // DirectionSetBlob.ResolveSlot is the per-set fold of DirectionFacing_System.md §5: the actor's
    // facing has already been quantized at the actor's own direction count, and this folds it again
    // into what THIS set actually authored. Without it a Two-coverage set on a Six-turning unit
    // returns an empty ClipId for every rear facing, which reads on screen as the unit freezing
    // whenever it walks away from the camera — the exact failure this pins.
    [TestFixture]
    public sealed class DirectionSetBlobFoldTests
    {
        private static readonly ClipId SouthEastClip = new ClipId(11UL);
        private static readonly ClipId NorthEastClip = new ClipId(22UL);
        private static readonly ClipId SouthClip = new ClipId(33UL);

        private static DirectionSetBlob TwoCoverageSet()
        {
            return new DirectionSetBlob
            {
                southEast = SouthEastClip,
                effectiveDirections = AnimationDirections.Two,
            };
        }

        [Test]
        public void TwoCoverageSet_FoldsARearFacingOntoItsFrontThreeQuarter()
        {
            DirectionSetBlob directionSet = TwoCoverageSet();

            Assert.AreEqual(SouthEastClip, directionSet.ResolveSlot(Direction.NorthEast));
        }

        [Test]
        public void TwoCoverageSet_FoldsHeadOnOntoItsFrontThreeQuarter()
        {
            DirectionSetBlob directionSet = TwoCoverageSet();

            Assert.AreEqual(SouthEastClip, directionSet.ResolveSlot(Direction.South));
        }

        [Test]
        public void OneCoverageSet_PlaysItsSouthClipForEveryFacing()
        {
            DirectionSetBlob directionSet = new DirectionSetBlob
            {
                south = SouthClip,
                effectiveDirections = AnimationDirections.One,
            };

            Assert.AreEqual(SouthClip, directionSet.ResolveSlot(Direction.SouthEast));
            Assert.AreEqual(SouthClip, directionSet.ResolveSlot(Direction.North));
            Assert.AreEqual(SouthClip, directionSet.ResolveSlot(Direction.East));
        }

        [Test]
        public void FullCoverageSet_KeepsTheFacingItWasAskedFor()
        {
            // The fold must be a no-op when the set covers what the actor turns through, or a
            // six-direction character would quietly lose its rear art.
            DirectionSetBlob directionSet = new DirectionSetBlob
            {
                southEast = SouthEastClip,
                northEast = NorthEastClip,
                south = SouthClip,
                north = new ClipId(44UL),
                effectiveDirections = AnimationDirections.Six,
            };

            Assert.AreEqual(NorthEastClip, directionSet.ResolveSlot(Direction.NorthEast));
            Assert.AreEqual(SouthClip, directionSet.ResolveSlot(Direction.South));
        }
    }
}
