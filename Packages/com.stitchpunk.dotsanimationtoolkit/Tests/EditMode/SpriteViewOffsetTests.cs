// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>ClipSampler.ResolveViewSlice</c> — the facing term of the slice sum
    /// (architecture section 5.7, amendment A37).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic is small and the failure it prevents is not: a character wearing another
    /// character's ears. That defect is invisible to every automated check that does not do this
    /// maths explicitly — the slice is a valid index into a real texture array, the mesh renders, and
    /// only a human who knows what a pointy ear looks like can see anything wrong. So the wrap is
    /// tested here, exhaustively and cheaply, rather than left to a play-mode eyeball.
    /// </para>
    /// <para>
    /// The fixture layout throughout is the ear array of A37's worked example:
    /// <c>[0]=pointy_front, [1]=pointy_back, [2]=round_front, [3]=round_back</c>, so
    /// <c>framesPerVariant = 2</c> and the even indices are the front views.
    /// </para>
    /// </remarks>
    public sealed class SpriteViewOffsetTests
    {
        private const int EarFramesPerVariant = 2;

        private const int PointyFront = 0;
        private const int PointyBack = 1;
        private const int RoundFront = 2;
        private const int RoundBack = 3;

        /// <summary>
        /// Catches: applying the offset without wrapping. This is the whole reason the package owns
        /// the maths — a citizen with round ears turned to face away would step from index 3 to 4,
        /// which is the first frame of the <em>next</em> ear shape.
        /// </summary>
        [Test]
        public void SteppingPastTheEndOfAVariant_WrapsInsideIt_RatherThanIntoTheNextVariant()
        {
            int resolved = ClipSampler.ResolveViewSlice(
                composedSlice: RoundBack,
                restSliceIndex: RoundBack,
                viewOffset: 1,
                framesPerVariant: EarFramesPerVariant);

            Assert.AreEqual(
                RoundFront,
                resolved,
                "Index 4 is the next shape's front view — a citizen would swap ears by turning round.");
        }

        /// <summary>
        /// The ordinary case: a front-facing round ear turns away and shows the round back view,
        /// not the pointy one. Catches: deriving the block from 0 rather than from the rest slice,
        /// which would send every character to the first variant's views.
        /// </summary>
        [Test]
        public void AViewOffset_StepsWithinTheCharactersOwnVariant()
        {
            int resolved = ClipSampler.ResolveViewSlice(
                composedSlice: RoundFront,
                restSliceIndex: RoundFront,
                viewOffset: 1,
                framesPerVariant: EarFramesPerVariant);

            Assert.AreEqual(RoundBack, resolved);
        }

        /// <summary>
        /// The same offset on a different character must land in that character's block. Catches:
        /// hardcoding a block base, which passes for whichever variant the fixture happened to use.
        /// </summary>
        [Test]
        public void TheSameOffset_LandsInADifferentBlockForADifferentVariant()
        {
            int pointyResolved = ClipSampler.ResolveViewSlice(
                PointyFront, PointyFront, 1, EarFramesPerVariant);
            int roundResolved = ClipSampler.ResolveViewSlice(
                RoundFront, RoundFront, 1, EarFramesPerVariant);

            Assert.AreEqual(PointyBack, pointyResolved);
            Assert.AreEqual(RoundBack, roundResolved);
            Assert.AreNotEqual(
                pointyResolved,
                roundResolved,
                "Two characters facing the same way must still have different ears.");
        }

        /// <summary>
        /// Catches: using C#'s <c>%</c> directly. It keeps the sign of the dividend, so stepping
        /// backwards from a block's first frame yields a negative remainder and lands below the
        /// block — on the previous variant, or out of the array entirely.
        /// </summary>
        [Test]
        public void ANegativeOffset_WrapsUpwardsInsideTheBlock()
        {
            int resolved = ClipSampler.ResolveViewSlice(
                composedSlice: RoundFront,
                restSliceIndex: RoundFront,
                viewOffset: -1,
                framesPerVariant: EarFramesPerVariant);

            Assert.AreEqual(
                RoundBack,
                resolved,
                "Naive % gives -1 here, which is below this variant's block and outside the array.");
        }

        /// <summary>
        /// Catches: wrapping against the composed slice's own block instead of the rest slice's. A
        /// relative animation key has already moved the composed value by the time this runs, and
        /// flooring *that* would let a large key silently redefine which variant the part belongs to.
        /// </summary>
        [Test]
        public void TheBlockComesFromTheRestSlice_NotFromTheComposedSlice()
        {
            // A relative key pushed the composed slice two frames past the character's own block.
            int resolved = ClipSampler.ResolveViewSlice(
                composedSlice: RoundFront + 2,
                restSliceIndex: RoundFront,
                viewOffset: 0,
                framesPerVariant: EarFramesPerVariant);

            Assert.AreEqual(
                RoundFront,
                resolved,
                "The character's variant is named by its rest slice; the key wraps back inside it.");
        }

        /// <summary>
        /// Catches: treating a zero offset as anything but identity. Every part on a rig that never
        /// opted into facing runs through this path if a caller forgets the HasComponent check, and
        /// it must be a no-op.
        /// </summary>
        [Test]
        public void AZeroOffset_ChangesNothing()
        {
            Assert.AreEqual(
                RoundFront,
                ClipSampler.ResolveViewSlice(RoundFront, RoundFront, 0, EarFramesPerVariant));
        }

        /// <summary>
        /// Catches: dividing by <c>framesPerVariant</c> unguarded. 1 means "this target has no
        /// variant blocks", which is the default for every target and must degrade to a plain sum
        /// rather than collapsing every offset to the rest slice.
        /// </summary>
        [Test]
        public void WithNoVariantBlocks_TheOffsetIsAPlainAddition()
        {
            Assert.AreEqual(7, ClipSampler.ResolveViewSlice(5, 5, 2, 1));
            Assert.AreEqual(7, ClipSampler.ResolveViewSlice(5, 5, 2, 0));
        }

        /// <summary>
        /// <strong>The fifth route to a negative slice, which A37 opened.</strong> Catches: dropping
        /// the lower clamp. Section 5.7 argued <c>SpriteMaterialSystem</c> needs no <c>-1</c> guard
        /// because all four routes to a negative index were closed by construction; a relative key or
        /// a negative view offset on an unblocked target is a fifth, so the clamp here is what keeps
        /// that argument true downstream.
        /// </summary>
        [Test]
        public void AnOffsetBelowZero_ClampsRatherThanIndexingOutOfTheArray()
        {
            Assert.AreEqual(0, ClipSampler.ResolveViewSlice(1, 1, -5, 1));
        }

        /// <summary>
        /// Catches: an offset larger than the block wrapping only once. Facing data is authored by
        /// hand, and a table row that says "+5" on a two-view target should still land on a real view
        /// of the right variant rather than somewhere arbitrary.
        /// </summary>
        [Test]
        public void AnOffsetLargerThanTheBlock_StillLandsInsideIt()
        {
            Assert.AreEqual(
                RoundBack,
                ClipSampler.ResolveViewSlice(RoundFront, RoundFront, 5, EarFramesPerVariant),
                "5 steps around a 2-frame block is one step: front to back.");
            Assert.AreEqual(
                RoundFront,
                ClipSampler.ResolveViewSlice(RoundFront, RoundFront, 4, EarFramesPerVariant));
        }

        /// <summary>
        /// A wider block than the ears, so the fixture set does not silently depend on the
        /// two-frame case where "wrap" and "toggle" are the same operation.
        /// </summary>
        [Test]
        public void AFourViewTarget_WrapsAcrossAllFourViews()
        {
            const int HairFramesPerVariant = 4;
            const int SecondHairStyleFirstView = 4;

            Assert.AreEqual(
                SecondHairStyleFirstView + 3,
                ClipSampler.ResolveViewSlice(
                    SecondHairStyleFirstView, SecondHairStyleFirstView, 3, HairFramesPerVariant));
            Assert.AreEqual(
                SecondHairStyleFirstView,
                ClipSampler.ResolveViewSlice(
                    SecondHairStyleFirstView, SecondHairStyleFirstView, 4, HairFramesPerVariant),
                "A full lap returns to the same view.");
        }
    }
}
