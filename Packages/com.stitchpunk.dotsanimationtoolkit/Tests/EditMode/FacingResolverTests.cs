// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>FacingResolver</c> against amendment A38's direction tables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>These fixtures exist because the tables cannot be derived.</strong> The direction
    /// sets are owner decisions about what reads well on screen, not compass geometry: two
    /// directions means a side-scroller three-quarter pair tilted toward the camera, not a profile;
    /// four means diagonals with no head-on view at all. Reasoning from angles produces sets that
    /// look plausible and are wrong — that mistake was made three separate times during design and
    /// caught each time only by a human looking at the screen.
    /// </para>
    /// <para>
    /// A transcription slip here is invisible to every other check: the character still animates,
    /// still turns, and simply faces a subtly wrong way. So this file's job is to be the table,
    /// written out longhand, where a diff against the amendment is a one-line read.
    /// </para>
    /// </remarks>
    public sealed class FacingResolverTests
    {
        // -------------------------------------------------------------------------------------
        // Set membership. Snapping must never return a direction outside the character's set.
        // -------------------------------------------------------------------------------------

        [Test]
        public void One_CollapsesEveryFacingToSouth()
        {
            // One means "this does not turn" — a boss at the top of the screen, a stationary
            // effect — so it faces the camera head-on rather than taking Two's three-quarter view.
            for (int facingValue = 0; facingValue < 8; facingValue++)
            {
                Assert.AreEqual(
                    Direction.South,
                    FacingResolver.Snap((Direction)facingValue, AnimationDirections.One),
                    "Direction " + ((Direction)facingValue).ToString() + " must collapse to South.");
            }
        }

        [Test]
        public void Two_ResolvesOnlyToTheFrontThreeQuarterPair()
        {
            for (int facingValue = 0; facingValue < 8; facingValue++)
            {
                Direction snapped = FacingResolver.Snap((Direction)facingValue, AnimationDirections.Two);
                Assert.IsTrue(
                    snapped == Direction.SouthEast || snapped == Direction.SouthWest,
                    "Two must resolve to SE or SW only, but "
                    + ((Direction)facingValue).ToString() + " gave " + snapped.ToString() + ".");
            }
        }

        [Test]
        public void Four_ResolvesOnlyToDiagonals_NeverHeadOn()
        {
            // The defining property of Four: no head-on animations at all, just front and back at
            // an angle. A South or North leaking through here is the exact bug this catches.
            for (int facingValue = 0; facingValue < 8; facingValue++)
            {
                Direction snapped = FacingResolver.Snap((Direction)facingValue, AnimationDirections.Four);
                Assert.IsTrue(
                    snapped == Direction.SouthEast || snapped == Direction.NorthEast
                    || snapped == Direction.NorthWest || snapped == Direction.SouthWest,
                    "Four must resolve to a diagonal, but "
                    + ((Direction)facingValue).ToString() + " gave " + snapped.ToString() + ".");
            }
        }

        [Test]
        public void Six_HasHeadOnAndHeadAway_ButNoTrueProfile()
        {
            Assert.AreEqual(Direction.South, FacingResolver.Snap(Direction.South, AnimationDirections.Six));
            Assert.AreEqual(Direction.North, FacingResolver.Snap(Direction.North, AnimationDirections.Six));

            // East and West are absent from Six: a six-direction character never shows a pure side
            // view, so they fold onto the nearest three-quarter on the same side.
            Assert.AreEqual(Direction.SouthEast, FacingResolver.Snap(Direction.East, AnimationDirections.Six));
            Assert.AreEqual(Direction.SouthWest, FacingResolver.Snap(Direction.West, AnimationDirections.Six));
        }

        [Test]
        public void Eight_PassesEveryFacingThroughUnchanged()
        {
            for (int facingValue = 0; facingValue < 8; facingValue++)
            {
                Direction facing = (Direction)facingValue;
                Assert.AreEqual(facing, FacingResolver.Snap(facing, AnimationDirections.Eight));
            }
        }

        // -------------------------------------------------------------------------------------
        // Mirror mapping. The invariant the whole scheme rests on.
        // -------------------------------------------------------------------------------------

        [Test]
        public void EverySetIsClosedUnderMirroring()
        {
            // Authored count = self-symmetric members + one of each mirror pair. If a resolved clip
            // facing were ever on the west side, the character would need art nobody authored.
            AnimationDirections[] allSets =
            {
                AnimationDirections.One,
                AnimationDirections.Two,
                AnimationDirections.Four,
                AnimationDirections.Six,
                AnimationDirections.Eight
            };

            for (int setIndex = 0; setIndex < allSets.Length; setIndex++)
            {
                for (int facingValue = 0; facingValue < 8; facingValue++)
                {
                    FacingResolver.ResolveClipFacing(
                        (Direction)facingValue, allSets[setIndex],
                        out Direction clipFacing, out bool mirrorX);

                    Assert.IsTrue(
                        clipFacing != Direction.SouthWest
                        && clipFacing != Direction.NorthWest
                        && clipFacing != Direction.West,
                        "Set " + allSets[setIndex].ToString() + " facing "
                        + ((Direction)facingValue).ToString()
                        + " resolved to west-side clip " + clipFacing.ToString()
                        + ", which is never authored.");
                }
            }
        }

        [Test]
        public void SelfSymmetricFacings_AreNeverMirrored()
        {
            // South and North are their own mirrors. Mirroring one would flip a symmetric pose for
            // no reason, which reads as a character that subtly changes handedness when it turns.
            FacingResolver.ResolveClipFacing(
                Direction.South, AnimationDirections.Six, out Direction southClip, out bool southMirror);
            Assert.AreEqual(Direction.South, southClip);
            Assert.IsFalse(southMirror);

            FacingResolver.ResolveClipFacing(
                Direction.North, AnimationDirections.Six, out Direction northClip, out bool northMirror);
            Assert.AreEqual(Direction.North, northClip);
            Assert.IsFalse(northMirror);
        }

        [Test]
        public void WestSideFacings_PlayTheirEastCounterpartMirrored()
        {
            FacingResolver.ToAuthoredSide(Direction.SouthWest, out Direction southEast, out bool southMirror);
            Assert.AreEqual(Direction.SouthEast, southEast);
            Assert.IsTrue(southMirror);

            FacingResolver.ToAuthoredSide(Direction.NorthWest, out Direction northEast, out bool northMirror);
            Assert.AreEqual(Direction.NorthEast, northEast);
            Assert.IsTrue(northMirror);

            FacingResolver.ToAuthoredSide(Direction.West, out Direction east, out bool westMirror);
            Assert.AreEqual(Direction.East, east);
            Assert.IsTrue(westMirror);
        }

        // -------------------------------------------------------------------------------------
        // Movement quantization.
        // -------------------------------------------------------------------------------------

        [Test]
        public void NearZeroMovement_HoldsTheCurrentFacing()
        {
            // A character coming to rest must keep facing the way it was going. Snapping to a
            // default on the last frame of a stop is a visible twitch.
            Assert.AreEqual(
                Direction.NorthWest,
                FacingResolver.FromMovement(
                    new float2(0f, 0f), AnimationDirections.Eight, Direction.NorthWest));
        }

        [Test]
        public void StraightAtCamera_IsDecidedNotTied_ForFourDirections()
        {
            // Nearest-angle would leave this undefined between SE and SW and flicker on the
            // boundary. Under sign-based quantization it is simply one of them, every time.
            Direction first = FacingResolver.FromMovement(
                new float2(0f, -1f), AnimationDirections.Four, Direction.NorthEast);
            Direction second = FacingResolver.FromMovement(
                new float2(0f, -1f), AnimationDirections.Four, Direction.NorthEast);

            Assert.AreEqual(first, second, "Quantization must be deterministic.");
            Assert.IsTrue(
                first == Direction.SouthEast || first == Direction.SouthWest,
                "Moving toward the camera must resolve to a front diagonal, got " + first.ToString() + ".");
        }

        [Test]
        public void HorizontalMovement_ReadsAsProfileOnlyWhenTheSetHasOne()
        {
            Assert.AreEqual(
                Direction.East,
                FacingResolver.FromMovement(
                    new float2(1f, 0f), AnimationDirections.Eight, Direction.South),
                "Eight has a true profile.");

            Direction sixFacing = FacingResolver.FromMovement(
                new float2(1f, 0f), AnimationDirections.Six, Direction.South);
            Assert.AreNotEqual(Direction.East, sixFacing, "Six has no true profile.");
        }
    }
}
