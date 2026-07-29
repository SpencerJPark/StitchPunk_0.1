// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Burst;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Id → dense-index resolution over the baked registry (architecture section 4.3): binary
    /// search over the sorted id arrays, O(log n), Burst-compatible. Clip resolution runs only
    /// when commands are applied; target resolution runs only at bind/bake time. Per-frame paths
    /// always use the cached dense indices.
    /// </summary>
    [BurstCompile]
    public static class ClipRegistryUtil
    {
        /// <summary>
        /// Resolves a <see cref="ClipId"/> to its dense index into
        /// <see cref="ClipRegistryBlob.clips"/> by binary search over
        /// <see cref="ClipRegistryBlob.sortedClipIds"/>. The search position <em>is</em> the dense
        /// index, because both arrays are baked in the same ascending-clip-id order
        /// (architecture sections 4.2, 4.5.1).
        /// </summary>
        /// <param name="registry">The baked registry.</param>
        /// <param name="clipId">The clip id to resolve.</param>
        /// <param name="clipIndex">The dense clip index on success; −1 on failure.</param>
        /// <returns>True when the id resolved; false for an invalid id, an empty registry, or an unknown id.</returns>
        [BurstCompile]
        public static bool TryResolveClip(ref ClipRegistryBlob registry, ClipId clipId, out int clipIndex)
        {
            clipIndex = -1;
            if (!clipId.IsValid)
            {
                return false;
            }

            int lowBound = 0;
            int highBound = registry.sortedClipIds.Length - 1;
            while (lowBound <= highBound)
            {
                int middleIndex = lowBound + ((highBound - lowBound) >> 1);
                ulong middleValue = registry.sortedClipIds[middleIndex];
                if (middleValue == clipId.Value)
                {
                    clipIndex = middleIndex;
                    return true;
                }
                if (middleValue < clipId.Value)
                {
                    lowBound = middleIndex + 1;
                }
                else
                {
                    highBound = middleIndex - 1;
                }
            }
            return false;
        }

        /// <summary>
        /// Resolves a <see cref="TargetId"/> to its dense target index — the id's position in
        /// <see cref="ClipRegistryBlob.sortedTargetIds"/> — by binary search. Same shape as
        /// <see cref="TryResolveClip"/>; used only at bind/bake time (architecture section 4.3).
        /// </summary>
        /// <param name="registry">The baked registry.</param>
        /// <param name="targetId">The target id to resolve.</param>
        /// <param name="targetIndex">The dense target index on success; −1 on failure.</param>
        /// <returns>True when the id resolved; false for an invalid id, an empty registry, or an unknown id.</returns>
        [BurstCompile]
        public static bool ResolveTargetIndex(ref ClipRegistryBlob registry, TargetId targetId, out int targetIndex)
        {
            targetIndex = -1;
            if (!targetId.IsValid)
            {
                return false;
            }

            int lowBound = 0;
            int highBound = registry.sortedTargetIds.Length - 1;
            while (lowBound <= highBound)
            {
                int middleIndex = lowBound + ((highBound - lowBound) >> 1);
                uint middleValue = registry.sortedTargetIds[middleIndex];
                if (middleValue == targetId.Value)
                {
                    targetIndex = middleIndex;
                    return true;
                }
                if (middleValue < targetId.Value)
                {
                    lowBound = middleIndex + 1;
                }
                else
                {
                    highBound = middleIndex - 1;
                }
            }
            return false;
        }
    }
}
