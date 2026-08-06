// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Pins architecture section 5.10's LOD table, which <see cref="AnimationLodPolicy"/> is the
    /// sole expression of (build step C4.8).
    /// </summary>
    /// <remarks>
    /// These are the arithmetic assertions the systems that obey LOD deliberately do not carry.
    /// A level whose meaning drifts between the transform path and the VAT path is invisible in
    /// motion — both look plausible — so the table is tested once, here, without a World.
    /// </remarks>
    public sealed class AnimationLodPolicyTests
    {
        private const float Tolerance = 1e-4f;

        /// <summary>
        /// Catches: applying a scale at level 0. Full quality has to mean the actor's own rate
        /// verbatim, including the 0 that means "every frame" — the setting every actor runs at
        /// until a host tunes one.
        /// </summary>
        [Test]
        public void LevelZero_LeavesTheRequestedRateAlone()
        {
            Assert.AreEqual(0f, AnimationLodPolicy.EffectiveSampleRateHz(0, 0f), Tolerance);
            Assert.AreEqual(24f, AnimationLodPolicy.EffectiveSampleRateHz(0, 24f), Tolerance);
        }

        /// <summary>
        /// Catches: quartering at level 1, or halving at level 2 — the two levels are one line
        /// apart and produce animation that merely looks slightly cheaper either way.
        /// </summary>
        [Test]
        public void LevelsOneAndTwo_HalveAndQuarterAnExplicitRate()
        {
            Assert.AreEqual(12f, AnimationLodPolicy.EffectiveSampleRateHz(1, 24f), Tolerance, "level 1 halves");
            Assert.AreEqual(6f, AnimationLodPolicy.EffectiveSampleRateHz(2, 24f), Tolerance, "level 2 quarters");
        }

        /// <summary>
        /// <strong>The case that decides whether LOD does anything at all.</strong> Catches:
        /// halving an uncapped rate, which is 0 × 0.5 = 0 — still "every frame". Since 0 is the
        /// default for every actor that never opts into quantization, a LOD system that only scales
        /// explicit rates is a no-op on essentially all content while appearing to work.
        /// </summary>
        [Test]
        public void AnUncappedActor_GetsAnOutrightCapFromTheLevel()
        {
            Assert.AreEqual(
                AnimationLodPolicy.UncappedLevel1RateHz,
                AnimationLodPolicy.EffectiveSampleRateHz(1, 0f),
                Tolerance);
            Assert.AreEqual(
                AnimationLodPolicy.UncappedLevel2RateHz,
                AnimationLodPolicy.EffectiveSampleRateHz(2, 0f),
                Tolerance);
            Assert.Greater(
                AnimationLodPolicy.UncappedLevel1RateHz,
                AnimationLodPolicy.UncappedLevel2RateHz,
                "The caps must descend with the level, or LOD 2 costs more than LOD 1.");
        }

        /// <summary>
        /// Catches: expressing level 3's freeze as a rate of 0. <c>ClipSampler.ShouldSample</c>
        /// reads 0 as "sample every frame", so the most expensive level would become the *only*
        /// unquantized one — and VAT, which never freezes, would publish every frame at the
        /// distance where crowds are largest.
        /// </summary>
        [Test]
        public void LevelThree_ReportsTheQuarterRate_NotZero()
        {
            Assert.AreEqual(
                AnimationLodPolicy.EffectiveSampleRateHz(2, 24f),
                AnimationLodPolicy.EffectiveSampleRateHz(3, 24f),
                Tolerance);
            Assert.AreEqual(
                AnimationLodPolicy.UncappedLevel2RateHz,
                AnimationLodPolicy.EffectiveSampleRateHz(3, 0f),
                Tolerance,
                "Freezing is expressed by FreezesPose, never by a rate of zero.");
        }

        /// <summary>
        /// Catches: moving either threshold by one level. Snapping at level 1 makes every mid-range
        /// actor hard-cut its crossfades; snapping only at 3 leaves level 2 costing the same as
        /// level 1 in the sampler.
        /// </summary>
        [Test]
        public void BlendSnapping_StartsAtLevelTwo()
        {
            Assert.IsFalse(AnimationLodPolicy.SnapsBlendWeights(0));
            Assert.IsFalse(AnimationLodPolicy.SnapsBlendWeights(1));
            Assert.IsTrue(AnimationLodPolicy.SnapsBlendWeights(2));
            Assert.IsTrue(AnimationLodPolicy.SnapsBlendWeights(3));
        }

        /// <summary>
        /// Catches: freezing from level 2, which would strand every mid-distance actor on a stale
        /// pose — the most visible defect this table can produce, and one that only shows at a
        /// distance where nobody is looking closely.
        /// </summary>
        [Test]
        public void PoseFreezing_StartsAtLevelThree()
        {
            Assert.IsFalse(AnimationLodPolicy.FreezesPose(0));
            Assert.IsFalse(AnimationLodPolicy.FreezesPose(1));
            Assert.IsFalse(AnimationLodPolicy.FreezesPose(2));
            Assert.IsTrue(AnimationLodPolicy.FreezesPose(3));
        }

        /// <summary>
        /// Catches: snapping with <c>floor</c> or <c>round-half-down</c>. The midpoint has to
        /// resolve to the incoming clip, so a snapped blend reaches its destination at the same
        /// moment an unsnapped one is half way — which is what keeps a LOD change mid-blend from
        /// looking like the blend restarted.
        /// </summary>
        [Test]
        public void ASnappedWeight_RoundsToTheNearerEnd()
        {
            Assert.AreEqual(0f, AnimationLodPolicy.SnapBlendWeight(0f), Tolerance);
            Assert.AreEqual(0f, AnimationLodPolicy.SnapBlendWeight(0.49f), Tolerance);
            Assert.AreEqual(1f, AnimationLodPolicy.SnapBlendWeight(0.5f), Tolerance);
            Assert.AreEqual(1f, AnimationLodPolicy.SnapBlendWeight(1f), Tolerance);
        }

        /// <summary>
        /// Catches: an off-by-one on the band boundaries, or comparing against un-squared
        /// distances. Thresholds are inclusive at the lower edge, so an actor exactly on a boundary
        /// takes the cheaper level.
        /// </summary>
        [Test]
        public void DistanceBands_MapToTheirLevels()
        {
            float4 thresholds = new float4(100f, 400f, 900f, 0f);

            Assert.AreEqual(0, AnimationLodPolicy.LevelForDistanceSq(0f, in thresholds));
            Assert.AreEqual(0, AnimationLodPolicy.LevelForDistanceSq(99f, in thresholds));
            Assert.AreEqual(1, AnimationLodPolicy.LevelForDistanceSq(100f, in thresholds), "inclusive lower edge");
            Assert.AreEqual(1, AnimationLodPolicy.LevelForDistanceSq(399f, in thresholds));
            Assert.AreEqual(2, AnimationLodPolicy.LevelForDistanceSq(400f, in thresholds));
            Assert.AreEqual(3, AnimationLodPolicy.LevelForDistanceSq(900f, in thresholds));
            Assert.AreEqual(3, AnimationLodPolicy.LevelForDistanceSq(1e9f, in thresholds));
        }

        /// <summary>
        /// Catches: testing the bands from nearest outward. With a mis-authored non-ascending set
        /// that order returns the *first* match rather than the furthest, so a host that typed its
        /// thresholds in the wrong order would get level 1 for an actor a kilometre away — worse
        /// than the degraded-but-conservative answer, because it costs full rate at every distance.
        /// </summary>
        [Test]
        public void NonAscendingThresholds_DegradeToTheFurthestMatch()
        {
            float4 mistyped = new float4(900f, 400f, 100f, 0f);

            Assert.AreEqual(
                3,
                AnimationLodPolicy.LevelForDistanceSq(1000f, in mistyped),
                "A distant actor must still reach the cheapest level under a mis-authored set.");
        }
    }
}
