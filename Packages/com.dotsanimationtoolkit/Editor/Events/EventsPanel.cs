// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Hosts the event key catalog, inspector, and usage columns and forwards their selection and open requests between them.</summary>
    public sealed class EventsPanel : VisualElement, IDisposable
    {
        public event Action<UnityEngine.Object> OpenOwnerRequested;

        public EventKeyCatalogColumn Keys { get; }
        public EventKeyInspectorColumn Inspector { get; }
        public EventUsageColumn Usage { get; }

        private AnimEventKeyRegistry boundRegistry;

        public EventsPanel()
        {
            name = "events-panel";
            style.flexGrow = 1f;

            Keys = new EventKeyCatalogColumn();
            Inspector = new EventKeyInspectorColumn();
            Usage = new EventUsageColumn();
            Keys.EntrySelected += OnEntrySelected;
            Usage.OpenOwnerRequested += OnUsageOpenOwnerRequested;

            CoverPaneSplitView detailSplit = new CoverPaneSplitView("Events.Inspector", 0, 420f, TwoPaneSplitViewOrientation.Horizontal);
            detailSplit.style.flexGrow = 1f;
            detailSplit.Add(Inspector);
            detailSplit.Add(Usage);

            CoverPaneSplitView keysSplit = new CoverPaneSplitView("Events.Keys", 0, 260f, TwoPaneSplitViewOrientation.Horizontal);
            keysSplit.style.flexGrow = 1f;
            keysSplit.Add(Keys);
            keysSplit.Add(detailSplit);

            Add(keysSplit);
        }

        public void Bind()
        {
            Bind(VocabularyRegistryProvider.AnimEventKeys);
        }

        public void Bind(AnimEventKeyRegistry registry)
        {
            // Unsubscribe first so a re-dock of the panel does not double-subscribe to the static event.
            VocabularyRegistryProvider.RegistryChanged -= OnRegistryChanged;
            VocabularyRegistryProvider.RegistryChanged += OnRegistryChanged;

            boundRegistry = registry;
            Keys.Bind(registry);
            OnEntrySelected(Keys.SelectedEntry);
        }

        public void Dispose()
        {
            VocabularyRegistryProvider.RegistryChanged -= OnRegistryChanged;
            Usage.Dispose();
        }

        private void OnEntrySelected(AnimEventKeyEntry entry)
        {
            Inspector.Bind(boundRegistry, entry);
            Usage.Bind(entry != null ? entry.eventKey : 0u);
        }

        private void OnRegistryChanged()
        {
            Keys.RefreshRows();

            // Rebinding the same entry rebuilds the fields under the user's cursor, so only re-bind on an actual selection change.
            if (Keys.SelectedEntry != Inspector.BoundEntry)
            {
                OnEntrySelected(Keys.SelectedEntry);
            }
        }

        private void OnUsageOpenOwnerRequested(UnityEngine.Object owner)
        {
            OpenOwnerRequested?.Invoke(owner);
        }
    }
}
