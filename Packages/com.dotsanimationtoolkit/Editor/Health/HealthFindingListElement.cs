// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Lists Health findings as rows: severity dot, code, message, an optional fix button, and a locate button naming the asset.</summary>
    public sealed class HealthFindingListElement : VisualElement
    {
        public event Action<HealthFinding> FixApplied;

        public HealthFindingListElement()
        {
            name = "health-finding-list";
            style.flexGrow = 1f;
        }

        public void SetFindings(IReadOnlyList<HealthFinding> findings)
        {
        }
    }
}
