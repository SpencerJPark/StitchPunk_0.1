// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Wrap-correct event-marker crossing math, generalized to multi-wrap large deltas, reverse
    /// playback, Once clamping, and PingPong reflection. Pure, allocation-free, Burst-compatible;
    /// shared by <c>EventEmissionSystem</c>, tests, and the editor preview.
    /// </summary>
    [BurstCompile]
    public static class EventWrapMath
    {
        /// <summary>
        /// Collects every event marker crossed while playback advanced from
        /// <paramref name="previousTime"/> to <paramref name="currentTime"/>, appending marker
        /// indices to <paramref name="crossedEventIndices"/> in chronological crossing order (a
        /// marker may appear more than once when a large delta wraps more than once). Forward
        /// motion uses the half-open window (previous, current]; reverse mirrors it as
        /// [current, previous). A marker at normalized time 0 therefore fires on each loop wrap but
        /// not at initial play start, and a marker at 1 fires when the end is reached. Once clamps
        /// both times to [0, duration] and never wraps. PingPong checks both the forward and
        /// reflected legs; endpoint markers (0 and 1) fire once per reflection, never twice.
        /// Unresolved <see cref="LoopMode.UseClipDefault"/> collects nothing — resolve it first via
        /// <see cref="ClipSampler.ResolveLoopMode"/>.
        /// </summary>
        /// <param name="events">The clip's event markers, sorted ascending by normalized time.</param>
        /// <returns>The number of crossings appended by this call.</returns>
        [BurstCompile]
        public static int CollectCrossings(
            ref BlobArray<EventMarkerBlob> events,
            float previousTime,
            float currentTime,
            float duration,
            LoopMode resolvedLoopMode,
            ref NativeList<int> crossedEventIndices)
        {
            int initialLength = crossedEventIndices.Length;
            if (duration <= 0f || events.Length == 0 || previousTime == currentTime)
            {
                return 0;
            }

            switch (resolvedLoopMode)
            {
                case LoopMode.Once:
                    CollectOnce(ref events, previousTime, currentTime, duration, ref crossedEventIndices);
                    break;
                case LoopMode.Loop:
                    CollectLoop(ref events, previousTime, currentTime, duration, ref crossedEventIndices);
                    break;
                case LoopMode.PingPong:
                    CollectPingPong(ref events, previousTime, currentTime, duration, ref crossedEventIndices);
                    break;
                default:
                    break;
            }

            return crossedEventIndices.Length - initialLength;
        }

        private static void CollectOnce(
            ref BlobArray<EventMarkerBlob> events,
            float previousTime,
            float currentTime,
            float duration,
            ref NativeList<int> crossedEventIndices)
        {
            float clampedPrevious = math.clamp(previousTime, 0f, duration);
            float clampedCurrent = math.clamp(currentTime, 0f, duration);

            if (currentTime > previousTime)
            {
                for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                {
                    float markerTime = events[eventIndex].normalizedTime * duration;
                    if (markerTime > clampedPrevious && markerTime <= clampedCurrent)
                    {
                        crossedEventIndices.Add(eventIndex);
                    }
                }
            }
            else
            {
                for (int eventIndex = events.Length - 1; eventIndex >= 0; eventIndex--)
                {
                    float markerTime = events[eventIndex].normalizedTime * duration;
                    if (markerTime >= clampedCurrent && markerTime < clampedPrevious)
                    {
                        crossedEventIndices.Add(eventIndex);
                    }
                }
            }
        }

        private static void CollectLoop(
            ref BlobArray<EventMarkerBlob> events,
            float previousTime,
            float currentTime,
            float duration,
            ref NativeList<int> crossedEventIndices)
        {
            float previousNormalized = previousTime / duration;
            float currentNormalized = currentTime / duration;

            if (currentNormalized > previousNormalized)
            {
                int firstSegment = (int)math.floor(previousNormalized);
                int lastSegment = (int)math.floor(currentNormalized);
                for (int segmentIndex = firstSegment; segmentIndex <= lastSegment; segmentIndex++)
                {
                    for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                    {
                        float globalPosition = segmentIndex + events[eventIndex].normalizedTime;
                        if (globalPosition > previousNormalized && globalPosition <= currentNormalized)
                        {
                            crossedEventIndices.Add(eventIndex);
                        }
                    }
                }
            }
            else
            {
                int firstSegment = (int)math.floor(currentNormalized);
                int lastSegment = (int)math.floor(previousNormalized);
                for (int segmentIndex = lastSegment; segmentIndex >= firstSegment; segmentIndex--)
                {
                    for (int eventIndex = events.Length - 1; eventIndex >= 0; eventIndex--)
                    {
                        float globalPosition = segmentIndex + events[eventIndex].normalizedTime;
                        if (globalPosition >= currentNormalized && globalPosition < previousNormalized)
                        {
                            crossedEventIndices.Add(eventIndex);
                        }
                    }
                }
            }
        }

        private static void CollectPingPong(
            ref BlobArray<EventMarkerBlob> events,
            float previousTime,
            float currentTime,
            float duration,
            ref NativeList<int> crossedEventIndices)
        {
            float period = 2f * duration;

            if (currentTime > previousTime)
            {
                int firstPeriod = (int)math.floor(previousTime / period);
                int lastPeriod = (int)math.floor(currentTime / period);
                for (int periodIndex = firstPeriod; periodIndex <= lastPeriod; periodIndex++)
                {
                    float periodStart = periodIndex * period;
                    for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                    {
                        float forwardLegTime = periodStart + events[eventIndex].normalizedTime * duration;
                        if (forwardLegTime > previousTime && forwardLegTime <= currentTime)
                        {
                            crossedEventIndices.Add(eventIndex);
                        }
                    }
                    for (int eventIndex = events.Length - 1; eventIndex >= 0; eventIndex--)
                    {
                        float markerNormalized = events[eventIndex].normalizedTime;
                        if (markerNormalized <= 0f || markerNormalized >= 1f)
                        {
                            continue;
                        }
                        float backwardLegTime = periodStart + period - markerNormalized * duration;
                        if (backwardLegTime > previousTime && backwardLegTime <= currentTime)
                        {
                            crossedEventIndices.Add(eventIndex);
                        }
                    }
                }
            }
            else
            {
                int firstPeriod = (int)math.floor(currentTime / period);
                int lastPeriod = (int)math.floor(previousTime / period);
                for (int periodIndex = lastPeriod; periodIndex >= firstPeriod; periodIndex--)
                {
                    float periodStart = periodIndex * period;
                    for (int eventIndex = 0; eventIndex < events.Length; eventIndex++)
                    {
                        float markerNormalized = events[eventIndex].normalizedTime;
                        if (markerNormalized <= 0f || markerNormalized >= 1f)
                        {
                            continue;
                        }
                        float backwardLegTime = periodStart + period - markerNormalized * duration;
                        if (backwardLegTime >= currentTime && backwardLegTime < previousTime)
                        {
                            crossedEventIndices.Add(eventIndex);
                        }
                    }
                    for (int eventIndex = events.Length - 1; eventIndex >= 0; eventIndex--)
                    {
                        float forwardLegTime = periodStart + events[eventIndex].normalizedTime * duration;
                        if (forwardLegTime >= currentTime && forwardLegTime < previousTime)
                        {
                            crossedEventIndices.Add(eventIndex);
                        }
                    }
                }
            }
        }
    }
}
