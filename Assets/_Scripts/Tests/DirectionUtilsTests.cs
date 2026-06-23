using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    // Characterization tests: they lock the CURRENT quantization boundaries of DirectionUtils so a
    // future refactor can't silently flip a unit's facing. If a mapping below looks "wrong", confirm
    // against DirectionUtils.cs before changing the assertion — the game art depends on these exact bins.
    [TestFixture]
    public sealed class DirectionUtilsTests
    {
        [Test]
        public void Get8Direction_MapsCardinalsToExpectedFacings()
        {
            Assert.AreEqual(Direction.East,  DirectionUtils.Get8Direction(new Vector2(1f, 0f)));
            Assert.AreEqual(Direction.North, DirectionUtils.Get8Direction(new Vector2(0f, 1f)));
            Assert.AreEqual(Direction.West,  DirectionUtils.Get8Direction(new Vector2(-1f, 0f)));
            Assert.AreEqual(Direction.South, DirectionUtils.Get8Direction(new Vector2(0f, -1f)));
        }

        [Test]
        public void Get8Direction_MapsDiagonalToNorthEast()
        {
            Assert.AreEqual(Direction.NorthEast, DirectionUtils.Get8Direction(new Vector2(1f, 1f)));
        }

        [Test]
        public void Get4Direction_QuantizesCardinalsToIsometricDiagonals()
        {
            // The 4-direction bin is rotated 45 degrees, so the world cardinals land on screen diagonals.
            Assert.AreEqual(Direction.NorthEast, DirectionUtils.Get4Direction(new Vector2(1f, 0f)));
            Assert.AreEqual(Direction.NorthWest, DirectionUtils.Get4Direction(new Vector2(0f, 1f)));
            Assert.AreEqual(Direction.SouthWest, DirectionUtils.Get4Direction(new Vector2(-1f, 0f)));
            Assert.AreEqual(Direction.SouthEast, DirectionUtils.Get4Direction(new Vector2(0f, -1f)));
        }

        [Test]
        public void Get6Direction_MapsKeyDirections()
        {
            Assert.AreEqual(Direction.NorthEast, DirectionUtils.Get6Direction(new Vector2(1f, 0f)));
            Assert.AreEqual(Direction.North,     DirectionUtils.Get6Direction(new Vector2(0f, 1f)));
            Assert.AreEqual(Direction.SouthWest, DirectionUtils.Get6Direction(new Vector2(-1f, 0f)));
            Assert.AreEqual(Direction.South,     DirectionUtils.Get6Direction(new Vector2(0f, -1f)));
        }
    }
}
