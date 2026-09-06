// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>The pure time arithmetic behind dragging several cutscene items at once.</summary>
    public sealed class CutsceneSelectionMathTests
    {
        // Dragging a group past the start of the timeline must move it as one piece. Clamping each
        // key on its own instead would land 1.0 and 2.5 both on 0 and silently destroy the rhythm.
        [Test]
        public void ShiftTimes_ClampsAtZero_AndPreservesOrder()
        {
            List<float> times = new List<float> { 1f, 2.5f, 4f };
            List<int> selectedIndices = new List<int> { 0, 1 };

            CutsceneSelectionMath.ShiftTimes(times, selectedIndices, -5f);

            Assert.AreEqual(0f, times[0], 1e-5f, "The earliest selected time lands exactly on zero.");
            Assert.AreEqual(1.5f, times[1], 1e-5f, "The group keeps its 1.5s spacing.");
            Assert.AreEqual(4f, times[2], 1e-5f, "An unselected key is untouched.");
            Assert.Less(times[0], times[1], "Order survives the clamp.");
        }
    }
}
