// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Pure checks behind the Sprite Sheets tab: frame size mismatches and unique frame names.</summary>
    public static class SpriteSheetValidation
    {
        // Every offender's name, in list order; empty when all frames match expectedSize.
        public static List<string> FindSizeMismatches(IReadOnlyList<(string name, Vector2Int size)> frames, Vector2Int expectedSize)
        {
            throw new NotImplementedException();
        }

        // name when free, otherwise "name 1", "name 2", ... — the first counter not in takenNames.
        public static string DedupeFrameName(string name, IReadOnlyCollection<string> takenNames)
        {
            throw new NotImplementedException();
        }
    }
}
