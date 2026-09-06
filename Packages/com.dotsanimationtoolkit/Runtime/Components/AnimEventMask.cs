// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The set of event windows currently open on this actor, one bit per event key — the "is it
    /// happening now" channel; <see cref="AnimEventOutput"/> is "it just happened". Recomputed
    /// every frame, never accumulated; disabled whenever no window is open.
    /// </summary>
    public struct AnimEventMask : IComponentData, IEnableableComponent
    {
        public ulong bits; // bit n = key AnimEventMaskKeys.FirstMaskKey + n; test via AnimEventMaskKeys.IsOpen
    }

    /// <summary>
    /// Maps an event key to its bit in <see cref="AnimEventMask"/>: bit n = key (FirstMaskKey + n).
    /// Keys above <see cref="LastMaskKey"/> remain legal but are pulse-only — no bit, so they can never hold a window open.
    /// </summary>
    [BurstCompile]
    public static class AnimEventMaskKeys
    {
        public const uint FirstMaskKey = (uint)ReservedEventKeys.FirstUserKey;

        public const int MaskKeyCount = 64; // width of AnimEventMask.bits

        public const uint LastMaskKey = FirstMaskKey + MaskKeyCount - 1;

        [BurstCompile]
        public static bool IsMaskable(uint eventKey)
        {
            return eventKey >= FirstMaskKey && eventKey <= LastMaskKey;
        }

        /// <returns>A mask with exactly one bit set, or 0 for a pulse-only key.</returns>
        [BurstCompile]
        public static ulong BitOf(uint eventKey)
        {
            if (!IsMaskable(eventKey))
            {
                return 0UL;
            }
            return 1UL << (int)(eventKey - FirstMaskKey);
        }

        /// <returns>True when the key is maskable and its window is open; a pulse-only key always answers false.</returns>
        [BurstCompile]
        public static bool IsOpen(in AnimEventMask mask, uint eventKey)
        {
            ulong bit = BitOf(eventKey);
            return bit != 0UL && (mask.bits & bit) != 0UL;
        }

        /// <summary>Whether any key folded into <paramref name="keyBits"/> (via <see cref="BitOf"/>) is open.</summary>
        [BurstCompile]
        public static bool IsAnyOpen(in AnimEventMask mask, ulong keyBits)
        {
            return (mask.bits & keyBits) != 0UL;
        }
    }
}
