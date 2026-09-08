// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Which of a profile layer row's blocks or stop keys is in effect at a timeline second: the
    /// last of the two to start at or before it. Shared by auto locomotion's "authored wins" gate and
    /// the Cutscene Editor's preview reconstruction, so both agree on when a row is untouched.
    /// </summary>
    internal static class CutsceneLayerStateResolver
    {
        /// <summary>A row's state at one instant.</summary>
        internal enum RowState : byte
        {
            /// <summary>Neither a block nor a stop has started on this row yet.</summary>
            None = 0,

            /// <summary>A block is the most recent thing to start on this row.</summary>
            Block = 1,

            /// <summary>A stop key is the most recent thing to start on this row.</summary>
            Stopped = 2
        }

        /// <summary>
        /// Scans both lists (already filtered to one profile layer) and returns whichever started
        /// last at or before <paramref name="timeSeconds"/>. <paramref name="blockIndex"/> is the
        /// winning block's index into <paramref name="rowBlocks"/> when the result is
        /// <see cref="RowState.Block"/>, else -1.
        /// </summary>
        internal static RowState ResolveAt(
            List<CutsceneClipBlock> rowBlocks,
            List<CutsceneLayerStopKey> rowStops,
            float timeSeconds,
            out int blockIndex)
        {
            blockIndex = -1;
            RowState state = RowState.None;
            float bestStartTime = float.NegativeInfinity;

            if (rowBlocks != null)
            {
                for (int index = 0; index < rowBlocks.Count; index++)
                {
                    CutsceneClipBlock block = rowBlocks[index];
                    if (block != null && block.start <= timeSeconds && block.start >= bestStartTime)
                    {
                        bestStartTime = block.start;
                        state = RowState.Block;
                        blockIndex = index;
                    }
                }
            }

            if (rowStops != null)
            {
                for (int index = 0; index < rowStops.Count; index++)
                {
                    CutsceneLayerStopKey stop = rowStops[index];
                    // >= so a stop authored at the exact same instant as a block wins the tie —
                    // both readings are defensible with nothing in the spec to prefer one, and this
                    // one is deterministic regardless of list order.
                    if (stop.time <= timeSeconds && stop.time >= bestStartTime)
                    {
                        bestStartTime = stop.time;
                        state = RowState.Stopped;
                        blockIndex = -1;
                    }
                }
            }

            return state;
        }
    }
}
