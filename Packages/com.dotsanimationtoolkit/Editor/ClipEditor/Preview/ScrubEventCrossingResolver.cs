// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Decides which event markers a scrub or playback step from one normalized time to another
    /// crosses, agreeing with the runtime wrap rule for looped playback.
    /// </summary>
    public static class ScrubEventCrossingResolver
    {
        private static readonly List<int> scratchIndices = new List<int>();

        public static void Resolve(float previousNormalized, float currentNormalized, bool isPlaying,
            LoopMode loop, IReadOnlyList<EventMarker> markers, List<int> crossedIndices)
        {
            if (markers == null || markers.Count == 0 || crossedIndices == null || previousNormalized == currentNormalized)
            {
                return;
            }

            float delta = currentNormalized - previousNormalized;
            float absoluteDelta = delta < 0f ? -delta : delta;

            // A scrub while stopped never wraps, no matter the loop mode: a large jump is a seek.
            if (!isPlaying && absoluteDelta > 0.5f)
            {
                return;
            }

            if (isPlaying && loop == LoopMode.Loop && absoluteDelta > 0.5f)
            {
                if (delta < 0f)
                {
                    CollectRange(markers, previousNormalized, false, 1f, true, true, crossedIndices);
                    CollectRange(markers, 0f, true, currentNormalized, true, true, crossedIndices);
                }
                else
                {
                    CollectRange(markers, 0f, true, previousNormalized, false, false, crossedIndices);
                    CollectRange(markers, currentNormalized, true, 1f, true, false, crossedIndices);
                }

                return;
            }

            if (delta > 0f)
            {
                CollectRange(markers, previousNormalized, false, currentNormalized, true, true, crossedIndices);
            }
            else
            {
                CollectRange(markers, currentNormalized, true, previousNormalized, false, false, crossedIndices);
            }
        }

        private static void CollectRange(IReadOnlyList<EventMarker> markers, float lowerBound, bool lowerInclusive,
            float upperBound, bool upperInclusive, bool ascending, List<int> crossedIndices)
        {
            scratchIndices.Clear();

            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                float markerTime = markers[markerIndex].normalizedTime;
                bool aboveLower = lowerInclusive ? markerTime >= lowerBound : markerTime > lowerBound;
                bool belowUpper = upperInclusive ? markerTime <= upperBound : markerTime < upperBound;
                if (aboveLower && belowUpper)
                {
                    scratchIndices.Add(markerIndex);
                }
            }

            scratchIndices.Sort((int leftIndex, int rightIndex) =>
            {
                float leftTime = markers[leftIndex].normalizedTime;
                float rightTime = markers[rightIndex].normalizedTime;
                int timeComparison = leftTime.CompareTo(rightTime);
                if (timeComparison != 0)
                {
                    return ascending ? timeComparison : -timeComparison;
                }

                return ascending ? leftIndex.CompareTo(rightIndex) : rightIndex.CompareTo(leftIndex);
            });

            for (int scratchIndex = 0; scratchIndex < scratchIndices.Count; scratchIndex++)
            {
                crossedIndices.Add(scratchIndices[scratchIndex]);
            }
        }
    }
}
