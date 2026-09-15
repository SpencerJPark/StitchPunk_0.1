// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's right column: the selected key's routes in the project routing asset, and the consumer stub button.</summary>
    public sealed class EventRoutesColumn : VisualElement
    {
        public AnimEventKeyEntry BoundEntry { get; private set; }

        // STUB (A93-T1): T9 replaces every body below.
        public EventRoutesColumn()
        {
            name = "events-routes-column";
        }

        public void Bind(AnimEventKeyEntry entry)
        {
            BoundEntry = entry;
        }

        public void RefreshRows()
        {
        }
    }
}
