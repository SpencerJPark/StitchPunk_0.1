// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's middle column: one event's fields, payload schema, preview clip and where it is used.</summary>
    public sealed class EventKeyInspectorColumn : VisualElement
    {
        public AnimEventKeyRegistry Registry { get; private set; }

        public AnimEventKeyEntry BoundEntry { get; private set; }

        // STUB (A93-T1): T8 replaces every body below.
        public EventKeyInspectorColumn()
        {
            name = "events-inspector-column";
        }

        public void Bind(AnimEventKeyRegistry registry, AnimEventKeyEntry entry)
        {
            Registry = registry;
            BoundEntry = entry;
        }

        public void RefreshUsage()
        {
        }
    }
}
