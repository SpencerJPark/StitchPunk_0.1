// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Mints the random stable ids that replace enum-ordinal identity (architecture section 3.4).
    /// An id is produced by folding a fresh <see cref="Guid"/> — the GUID's high and low 64-bit
    /// halves are exclusive-ored together — so ids are random rather than name-derived and a rename,
    /// a list reorder, or an asset move can never change identity. The value 0 is reserved as
    /// "none/invalid" on both <see cref="ClipId"/> and <see cref="TargetId"/>, so generation retries
    /// on the (vanishingly improbable) fold that lands on zero.
    /// </summary>
    /// <remarks>
    /// This is the runtime half of the identity scheme: it lives in the Runtime assembly so the
    /// identity structs and their generator ship together, but it is owned by the authoring module
    /// (architecture sections 1.2 and 8 M1) and is called from asset creation, never per frame.
    /// It allocates (<see cref="Guid.NewGuid"/> plus a 16-byte array) and must not be called from
    /// Burst-compiled code.
    /// </remarks>
    public static class StableIdUtility
    {
        /// <summary>
        /// Folds a GUID into a 64-bit id by exclusive-oring its high and low 64-bit halves. The
        /// halves are assembled byte by byte in little-endian order, so the result depends only on
        /// the GUID's bytes and never on the host machine's endianness.
        /// </summary>
        /// <param name="guid">The GUID to fold.</param>
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

        /// <summary>
        /// Mints a fresh non-zero 64-bit stable id for an identity-bearing asset
        /// (<c>ClipAsset</c>, <c>RigAsset</c>, <c>ClipSetAsset</c>, <c>VatTextureSetAsset</c>).
        /// </summary>
        /// <returns>A random id that is never 0.</returns>
        public static ulong NewAssetStableId()
        {
            ulong foldedId = 0UL;
            while (foldedId == 0UL)
            {
                foldedId = Fold(Guid.NewGuid());
            }
            return foldedId;
        }

        /// <summary>
        /// Mints a fresh non-zero 32-bit stable id for a rig target row. Per-rig scope makes 32 bits
        /// ample, so the folded value is truncated to its low 32 bits (architecture section 3.4).
        /// </summary>
        /// <returns>A random id that is never 0.</returns>
        public static uint NewTargetStableId()
        {
            uint truncatedId = 0u;
            while (truncatedId == 0u)
            {
                truncatedId = (uint)Fold(Guid.NewGuid());
            }
            return truncatedId;
        }

        /// <summary>Mints a fresh non-zero <see cref="ClipId"/>.</summary>
        /// <returns>A random clip id that is never <c>0</c>.</returns>
        public static ClipId NewClipId()
        {
            return new ClipId(NewAssetStableId());
        }

        /// <summary>Mints a fresh non-zero <see cref="TargetId"/>.</summary>
        /// <returns>A random target id that is never <c>0</c>.</returns>
        public static TargetId NewTargetId()
        {
            return new TargetId(NewTargetStableId());
        }
    }
}
