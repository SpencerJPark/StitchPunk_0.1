// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Health tab: Scan, per-severity counts and filters, a text filter and the findings list; rescans shortly after toolkit assets change.</summary>
    public sealed class HealthPanel : VisualElement, IDisposable
    {
        public event Action<ClipSetAsset, RigAsset> RebakeRequested;
        public event Action FindingsChanged;

        private readonly List<HealthFinding> latestFindings = new List<HealthFinding>();

        public IReadOnlyList<HealthFinding> LatestFindings
        {
            get { return latestFindings; }
        }

        public HealthPanel()
        {
            name = "health-panel";
            style.flexGrow = 1f;
        }

        public void Bind()
        {
        }

        public void Scan()
        {
        }

        public void Dispose()
        {
        }
    }
}
