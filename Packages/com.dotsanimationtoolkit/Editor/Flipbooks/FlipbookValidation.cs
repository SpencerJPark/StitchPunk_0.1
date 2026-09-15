// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Pure checks behind the Flipbooks tab: frame size mismatches and unique frame names.</summary>
    public static class FlipbookValidation
    {
        // Every offender's name, in list order; empty when all frames match expectedSize.
        public static List<string> FindSizeMismatches(IReadOnlyList<(string name, Vector2Int size)> frames, Vector2Int expectedSize)
        {
            List<string> mismatchedFrameNames = new List<string>();
            if (frames == null)
            {
                return mismatchedFrameNames;
            }

            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                if (frames[frameIndex].size != expectedSize)
                {
                    mismatchedFrameNames.Add(frames[frameIndex].name);
                }
            }

            return mismatchedFrameNames;
        }

        // name when free, otherwise "name 1", "name 2", ... — the first counter not in takenNames.
        public static string DedupeFrameName(string name, IReadOnlyCollection<string> takenNames)
        {
            if (takenNames == null || !takenNames.Contains(name))
            {
                return name;
            }

            int suffixCounter = 1;
            string candidateName = name + " " + suffixCounter;
            while (takenNames.Contains(candidateName))
            {
                suffixCounter++;
                candidateName = name + " " + suffixCounter;
            }

            return candidateName;
        }
    }
}
