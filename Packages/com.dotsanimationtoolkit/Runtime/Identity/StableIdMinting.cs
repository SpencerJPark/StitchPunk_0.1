// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Mints the random stable ids that replace enum-ordinal identity. An id folds a fresh
    /// <see cref="Guid"/> (XOR of its high/low 64-bit halves) so a rename, reorder, or asset move
    /// can never change it. Allocates; never call from Burst-compiled code.
    /// </summary>
    public static class StableIdMinting
    {
        /// <returns>The folded 64-bit value; may legitimately be 0 for a pathological GUID.</returns>
        public static ulong Fold(Guid guid)
        {
            byte[] guidBytes = guid.ToByteArray();
            ulong lowHalf = 0UL;
            ulong highHalf = 0UL;
            for (int byteIndex = 0; byteIndex < 8; byteIndex++)
            {
                lowHalf |= (ulong)guidBytes[byteIndex] << (byteIndex * 8);
                highHalf |= (ulong)guidBytes[byteIndex + 8] << (byteIndex * 8);
            }
            return lowHalf ^ highHalf;
        }
        
        public static ulong NewAssetStableId()
        {
            ulong foldedId = 0UL;
            while (foldedId == 0UL)
            {
                foldedId = Fold(Guid.NewGuid());
            }
            return foldedId;
        }
        
        public static uint NewTargetStableId()
        {
            uint truncatedId = 0u;
            while (truncatedId == 0u)
            {
                truncatedId = (uint)Fold(Guid.NewGuid());
            }
            return truncatedId;
        }

        public static ClipId NewClipId()
        {
            return new ClipId(NewAssetStableId());
        }

        public static TargetId NewTargetId()
        {
            return new TargetId(NewTargetStableId());
        }
    }
}
