// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Resolves a baked event key to its routed range: binary search over the sorted key
    /// array, O(log n).
    /// </summary>
    [BurstCompile]
    public static class AnimEventRoutingApi
    {
        [BurstCompile]
        public static bool TryGetRoutes(ref AnimEventRoutingBlob routing, uint eventKey, out int routeStart, out int routeCount)
        {
            routeStart = 0;
            routeCount = 0;
            // An `in` reference lets Burst make defensive copies of the blob whose internal
            // BlobArray offsets would then point at garbage, so this must stay `ref`.
            if (routing.keyStarts.Length != routing.keys.Length + 1)
            {
                return false;
            }

            int lowBound = 0;
            int highBound = routing.keys.Length - 1;
            while (lowBound <= highBound)
            {
                int middleIndex = lowBound + ((highBound - lowBound) >> 1);
                uint middleValue = routing.keys[middleIndex];
                if (middleValue == eventKey)
                {
                    routeStart = routing.keyStarts[middleIndex];
                    routeCount = routing.keyStarts[middleIndex + 1] - routing.keyStarts[middleIndex];
                    return true;
                }
                if (middleValue < eventKey)
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
