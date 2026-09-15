// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Burst-compatible key filters over an actor's AnimEventOutput buffer, so consumers skip writing the scan loop.</summary>
    public static class AnimEventBufferApi
    {
        public static bool ContainsEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey)
        {
            int searchIndex = 0;
            return TryFindNextEvent(events, eventKey, ref searchIndex, out AnimEventOutput foundEvent);
        }

        public static bool TryFindEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey, out AnimEventOutput foundEvent)
        {
            int searchIndex = 0;
            return TryFindNextEvent(events, eventKey, ref searchIndex, out foundEvent);
        }

        public static bool TryFindNextEvent(in DynamicBuffer<AnimEventOutput> events, uint eventKey, ref int searchIndex, out AnimEventOutput foundEvent)
        {
            int startIndex = searchIndex < 0 ? 0 : searchIndex;
            for (int index = startIndex; index < events.Length; index++)
            {
                if (events[index].eventKey == eventKey)
                {
                    foundEvent = events[index];
                    searchIndex = index + 1;
                    return true;
                }
            }

            foundEvent = default;
            searchIndex = events.Length;
            return false;
        }
    }
}
