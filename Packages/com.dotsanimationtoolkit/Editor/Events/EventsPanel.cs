// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Hosts the event key catalog, inspector, and routes columns and forwards their selection and change events between them.</summary>
    public sealed class EventsPanel : VisualElement, IDisposable
    {
        public EventKeyCatalogColumn Keys { get; }
        public EventKeyInspectorColumn Inspector { get; }
        public EventRoutesColumn Routes { get; }

        private AnimEventKeyRegistry boundRegistry;

        public EventsPanel()
        {
            name = "events-panel";
            style.flexGrow = 1f;

            Keys = new EventKeyCatalogColumn();
            Inspector = new EventKeyInspectorColumn();
            Routes = new EventRoutesColumn();
            Keys.EntrySelected += OnEntrySelected;

            CoverPaneSplitView detailSplit = new CoverPaneSplitView("Events.Inspector", 0, 420f, TwoPaneSplitViewOrientation.Horizontal);
            detailSplit.style.flexGrow = 1f;
            detailSplit.Add(Inspector);
            detailSplit.Add(Routes);

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
            // Unsubscribe first so a re-dock of the panel does not double-subscribe to the static events.
            VocabularyRegistryProvider.RegistryChanged -= OnRegistryChanged;
            AssetReferenceIndex.Rebuilt -= OnReferenceIndexRebuilt;
            AnimEventRoutingAssetUtility.RoutingChanged -= OnRoutingChanged;

            VocabularyRegistryProvider.RegistryChanged += OnRegistryChanged;
            AssetReferenceIndex.Rebuilt += OnReferenceIndexRebuilt;
            AnimEventRoutingAssetUtility.RoutingChanged += OnRoutingChanged;

            boundRegistry = registry;
            Keys.Bind(registry);
            OnEntrySelected(Keys.SelectedEntry);
        }

        public void Dispose()
        {
            VocabularyRegistryProvider.RegistryChanged -= OnRegistryChanged;
            AssetReferenceIndex.Rebuilt -= OnReferenceIndexRebuilt;
            AnimEventRoutingAssetUtility.RoutingChanged -= OnRoutingChanged;
        }

        private void OnEntrySelected(AnimEventKeyEntry entry)
        {
            Inspector.Bind(boundRegistry, entry);
            Routes.Bind(entry);
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

        private void OnReferenceIndexRebuilt()
        {
            Inspector.RefreshUsage();
        }

        private void OnRoutingChanged()
        {
            Routes.RefreshRows();
        }
    }
}
