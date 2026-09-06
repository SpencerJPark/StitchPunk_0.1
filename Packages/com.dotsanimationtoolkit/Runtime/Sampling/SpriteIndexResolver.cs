// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns a flipbook key's stored number into the array index it names. One function, three
    /// readers (the sampler, the clip editor, and validation) — if any computed it independently,
    /// the number an author sees could differ from the frame that plays.
    /// </summary>
    [BurstCompile]
    public static class SpriteIndexResolver
    {
        public const int NoChangeSentinel = -1; // absolute mode only: "leave the current frame alone"

        // NoChangeSentinel passes through untouched in absolute mode. In relative mode -1 is an
        // ordinary offset of one frame back, not a sentinel — there is nothing for "no change" to
        // mean when every key is a displacement.
        [BurstCompile]
        public static int Resolve(int storedValue, SpriteIndexMode indexMode, int baseIndex)
        {
            if (indexMode == SpriteIndexMode.RelativeToBase)
            {
                return baseIndex + storedValue;
            }
            return storedValue;
        }

        /// <summary>The value to store so that <paramref name="targetIndex"/> resolves under a given mode and base — the inverse of <see cref="Resolve"/>.</summary>
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
