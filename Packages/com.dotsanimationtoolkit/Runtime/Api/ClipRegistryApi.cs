// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Id to dense-index resolution over the baked registry: binary search over the sorted id
    /// arrays, O(log n). The search position is the dense index because both arrays are baked in
    /// the same ascending-id order.
    /// </summary>
    [BurstCompile]
    public static class ClipRegistryApi
    {
        /// <param name="clipIndex">The dense clip index on success; -1 on failure.</param>
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

        /// <param name="targetIndex">The dense target index on success; -1 on failure.</param>
        [BurstCompile]
        public static bool TryResolveTarget(ref ClipRegistryBlob registry, TargetId targetId, out int targetIndex)
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
