// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's Keys column: the event registry as a searchable catalog with New, Refresh and a row context menu.</summary>
    public sealed class EventKeyCatalogColumn : VisualElement
    {
        public event Action<AnimEventKeyEntry> EntrySelected;

        public AnimEventKeyRegistry Registry { get; private set; }

        public AnimEventKeyEntry SelectedEntry { get; private set; }

        // STUB (A93-T1): T7 replaces every body below.
        public EventKeyCatalogColumn()
        {
            name = "events-keys-column";
        }

        public void Bind(AnimEventKeyRegistry registry)
        {
            Registry = registry;
        }

        public void RefreshRows()
        {
        }

        public void SelectKey(uint eventKey)
        {
            EntrySelected?.Invoke(SelectedEntry);
        }
    }
}
