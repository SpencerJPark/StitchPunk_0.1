// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's right column: the clips, cutscenes and profiles that use the selected event, each with an open button.</summary>
    public sealed class EventUsageColumn : VisualElement, IDisposable
    {
        public event Action<UnityEngine.Object> OpenOwnerRequested;

        public uint BoundEventKey { get; private set; }

        public EventUsageColumn()
        {
            name = "event-usage-column";
            style.flexGrow = 1f;
        }

        public void Bind(uint eventKey)
        {
            BoundEventKey = eventKey;
        }

        public void Dispose()
        {
        }

        private void RaiseOpenOwnerRequested(UnityEngine.Object owner)
        {
            OpenOwnerRequested?.Invoke(owner);
        }
    }
}
