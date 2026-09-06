// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Pure time arithmetic behind a multi-item timeline drag, kept out of the panel so it can be tested without an Editor window.</summary>
    public static class CutsceneSelectionMath
    {
        /// <summary>
        /// Shifts the times at <paramref name="indices"/> by <paramref name="deltaSeconds"/>, as one
        /// rigid group: the delta is reduced until the earliest of them lands on zero, so a drag past
        /// the start of the timeline never collapses the group's spacing.
        /// </summary>
        public static void ShiftTimes(List<float> times, IReadOnlyList<int> indices, float deltaSeconds)
        {
            if (times == null || indices == null || indices.Count == 0)
            {
                return;
            }

            float earliestSelectedTime = float.MaxValue;
            for (int cursor = 0; cursor < indices.Count; cursor++)
            {
                int index = indices[cursor];
                if (index < 0 || index >= times.Count)
                {
                    continue;
                }
                if (times[index] < earliestSelectedTime)
                {
                    earliestSelectedTime = times[index];
                }
            }
            if (earliestSelectedTime == float.MaxValue)
            {
                return;
            }

            // Clamping each time on its own would pile every key that went negative onto zero, which
            // is a silent edit of the rhythm the author was dragging.
            float appliedDelta = deltaSeconds;
            if (earliestSelectedTime + appliedDelta < 0f)
            {
                appliedDelta = -earliestSelectedTime;
            }

            for (int cursor = 0; cursor < indices.Count; cursor++)
            {
                int index = indices[cursor];
                if (index < 0 || index >= times.Count)
                {
                    continue;
                }
                times[index] = times[index] + appliedDelta;
            }
        }
    }
}
