// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>CutsceneLayerStateResolver</c> — the pure "last block or stop at or before this
    /// time" rule the cutscene preview's per-layer reconstruction and auto locomotion's
    /// authored-wins gate both read.
    /// </summary>
    public sealed class CutsceneLayerReconstructionTests
    {
        /// <summary>
        /// Catches: resolving purely by list order instead of by time, or losing which block won
        /// once a stop key sits between two of them on the same row.
        /// </summary>
        [Test]
        public void ResolveAt_PicksTheLastBlockOrStop_AtOrBeforeTheTime()
        {
            List<CutsceneClipBlock> rowBlocks = new List<CutsceneClipBlock>
            {
                new CutsceneClipBlock { animationKey = 1u, start = 0f },
                new CutsceneClipBlock { animationKey = 1u, start = 3f }
            };
            List<CutsceneLayerStopKey> rowStops = new List<CutsceneLayerStopKey>
            {
                new CutsceneLayerStopKey { time = 2f }
            };

            int blockIndex;
            CutsceneLayerStateResolver.RowState stateAtOne =
                CutsceneLayerStateResolver.ResolveAt(rowBlocks, rowStops, 1f, out blockIndex);
            Assert.AreEqual(CutsceneLayerStateResolver.RowState.Block, stateAtOne);
            Assert.AreEqual(0, blockIndex, "The block starting at 0 is still the one playing at 1s.");

            CutsceneLayerStateResolver.RowState stateAtTwoAndHalf =
                CutsceneLayerStateResolver.ResolveAt(rowBlocks, rowStops, 2.5f, out blockIndex);
            Assert.AreEqual(CutsceneLayerStateResolver.RowState.Stopped, stateAtTwoAndHalf,
                "The stop at 2s is the most recent thing on the row.");

            CutsceneLayerStateResolver.RowState stateAtFour =
                CutsceneLayerStateResolver.ResolveAt(rowBlocks, rowStops, 4f, out blockIndex);
            Assert.AreEqual(CutsceneLayerStateResolver.RowState.Block, stateAtFour);
            Assert.AreEqual(1, blockIndex, "The block starting at 3 has superseded the earlier stop.");
        }

        /// <summary>
        /// Catches: defaulting an untouched row to <c>Stopped</c> rather than <c>None</c> — the
        /// distinction auto locomotion's fallback and the preview's locomotion row read to tell
        /// "never touched" from "explicitly stopped".
        /// </summary>
        [Test]
        public void ResolveAt_WithNothingAuthoredYet_ReturnsNone()
        {
            int blockIndex;
            CutsceneLayerStateResolver.RowState state = CutsceneLayerStateResolver.ResolveAt(
                new List<CutsceneClipBlock>(), new List<CutsceneLayerStopKey>(), 5f, out blockIndex);

            Assert.AreEqual(CutsceneLayerStateResolver.RowState.None, state);
            Assert.AreEqual(-1, blockIndex);
        }
    }
}
