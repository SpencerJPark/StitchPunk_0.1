// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns a flipbook key's stored number into the array index it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One function, three readers.</strong> The Burst sampler resolves keys to drive the
    /// material, the clip editor resolves them to show "+5 → 12" beside a key, and validation
    /// resolves them to check the result is in range. If any of those computed it themselves, the
    /// number an author reads in the editor could differ from the frame that plays — which is the
    /// single most confusing way for a flipbook to be wrong.
    /// </para>
    /// <para>
    /// This resolves the <em>track</em> value only. Whether that value replaces the pose's slice or
    /// is added to the character's rest slice is <see cref="SpriteSliceSpace"/>'s job, applied by
    /// the caller — the two compose, and keeping them separate is what lets an authored base and a
    /// runtime variant both retarget the same key.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class SpriteIndexResolver
    {
        /// <summary>The stored value meaning "leave the current frame alone", in absolute mode.</summary>
        public const int NoChangeSentinel = -1;

        /// <summary>
        /// The index a key names, given its mode and its track's base.
        /// </summary>
        /// <remarks>
        /// <see cref="NoChangeSentinel"/> passes through untouched in absolute mode so the caller
        /// can still recognise it. In relative mode −1 is an ordinary offset of one frame back, not
        /// a sentinel — there is nothing for "no change" to mean when every key is a displacement.
        /// </remarks>
        [BurstCompile]
        public static int Resolve(int storedValue, SpriteIndexMode indexMode, int baseIndex)
        {
            if (indexMode == SpriteIndexMode.RelativeToBase)
            {
                return baseIndex + storedValue;
            }
            return storedValue;
        }

        /// <summary>
        /// The value to store so that <paramref name="targetIndex"/> resolves under a given mode
        /// and base — the inverse of <see cref="Resolve"/>.
        /// </summary>
        /// <remarks>
        /// This is what makes toggling a key's mode lossless in the sense that matters: the frame
        /// it shows does not move. Absolute→Relative subtracts the base to recover the offset;
        /// Relative→Absolute writes the resolved value out. Both preserve the resolved index, which
        /// is the thing an author is actually looking at.
        /// </remarks>
        [BurstCompile]
        public static int StoredValueFor(int targetIndex, SpriteIndexMode indexMode, int baseIndex)
        {
            if (indexMode == SpriteIndexMode.RelativeToBase)
            {
                return targetIndex - baseIndex;
            }
            return targetIndex;
        }
    }
}
