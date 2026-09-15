// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Burst-compatible key filters over an actor's AnimEventOutput buffer, so consumers skip writing the scan loop.</summary>
    public static class AnimEventBufferApi
    {
        public static bool ContainsEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey)
        {
            return false;
        }

        public static bool TryFindEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey, out AnimEventOutput foundEvent)
        {
            foundEvent = default;
            return false;
        }

        public static bool TryFindNextEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey, ref int searchIndex, out AnimEventOutput foundEvent)
        {
            foundEvent = default;
            return false;
        }
    }
}
