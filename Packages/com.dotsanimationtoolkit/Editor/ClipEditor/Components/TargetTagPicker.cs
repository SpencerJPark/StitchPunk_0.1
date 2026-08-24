// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The searchable tag picker (Phase E target-tags spec §4.2.1): the one control that may ever
    /// choose a <see cref="TargetTagEntry.stableId"/> for a rig target's <c>tagId</c> (E2) or, once
    /// E3 lands, a track's tag binding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Selection only, never typing — this is the whole safety argument for §6.1's lenient
    /// T2.</strong> A tag's name is typed exactly once, in the registry, when the tag is defined
    /// (spec §4.2.1). Every other surface offers the existing tags and nothing else, so a wrong tag
    /// is a wrong <em>pick</em> from a visible list — a mistake you can see — rather than a typo that
    /// resolves to nothing and gets skipped with a warning nobody reads.
    /// </para>
    /// <para>
    /// <strong>Built on <see cref="PickerOverlay"/>, the base <see cref="ClipComponentPicker"/> was
    /// generalised into.</strong> The overlay chrome — dismiss-on-outside-press, Escape, the hover
    /// card, panel placement and its overhang clamp — is identical between the two pickers; only the
    /// row content and the filter field are new here. See <see cref="PickerOverlay"/>'s remarks for
    /// why the split falls where it does.
    /// </para>
    /// <para>
    /// <strong>Three fixed rows bracket the filtered tag list.</strong> A "(none)" row is always
    /// first and never filtered away, because untagged is a legal, ordinary state (spec §4.2) and a
    /// picker that could not clear a tag would force every mis-tagged target through the registry
    /// instead. "Edit tags…" is always last, per spec §4.2.1 — discovering mid-tagging that a role
    /// has no tag yet must not mean abandoning the rig to go hunt for the registry asset. Between
    /// them, when the filter matches nothing, an optional "Create tag '&lt;text&gt;'" row lets the
    /// tag be defined on the spot — spec §4.2.1 allows this because it is still one typing surface,
    /// the tag's definition, not a second way to spell an existing one.
    /// </para>
    /// </remarks>
    public sealed class TargetTagPicker : PickerOverlay
    {
        private const float PanelWidth = 220f;
        private const float CardWidth = 240f;

        private const string NoneRowLabel = "(none)";
        private const string EditTagsRowLabel = "Edit tags…";

        private readonly TargetTagRegistry registry;
        private readonly Action<uint> onPick;
        private readonly Action onRegistryChanged;
        private readonly TextField filterField;
        private readonly VisualElement rowsContainer;

        private TargetTagPicker(
            TargetTagRegistry registry, Action<uint> onPick, Action onRegistryChanged)
            : base(PanelWidth, CardWidth)
        {
            this.registry = registry;
            this.onPick = onPick;
            this.onRegistryChanged = onRegistryChanged;

            filterField = new TextField();
            filterField.style.marginLeft = 4f;
            filterField.style.marginRight = 4f;
            filterField.style.marginTop = 2f;
            filterField.style.marginBottom = 2f;
            // Filtering only ever narrows the list; it never binds anything by itself (spec §4.2.1).
            filterField.RegisterValueChangedCallback(changeEvent => RefreshRows());
            listPanel.Add(filterField);

            rowsContainer = new VisualElement();
            listPanel.Add(rowsContainer);

            RefreshRows();
        }

        /// <summary>Opens the picker over <paramref name="host"/>, hung under <paramref name="anchor"/>.</summary>
        /// <param name="host">The element the overlay covers and is placed within.</param>
        /// <param name="anchor">The control that opened it; the panel hangs from its lower-left.</param>
        /// <param name="registry">
        /// The project's tag registry. A null or empty registry still opens — the list is then just
        /// "(none)", "Create tag…" and "Edit tags…" — because a project with no tags yet is exactly
        /// when a person most needs the in-flow create-and-edit path.
        /// </param>
        /// <param name="onPick">
        /// Invoked with the chosen tag's stable id, or 0 for "(none)". Not invoked when the picker is
        /// dismissed without a choice, or when "Edit tags…" is chosen.
        /// </param>
        /// <param name="onRegistryChanged">
        /// Invoked after a tag is minted through "Create tag…", or after the "Edit tags…" window is
        /// closed — either can change what a caller's own cached tag names should show.
        /// </param>
        public static TargetTagPicker Open(
            VisualElement host,
            VisualElement anchor,
            TargetTagRegistry registry,
            Action<uint> onPick,
            Action onRegistryChanged)
        {
            if (host == null)
            {
                return null;
            }
            TargetTagPicker picker = new TargetTagPicker(registry, onPick, onRegistryChanged);
            picker.FinalizeOpen(host, anchor);
            return picker;
        }

        private void RefreshRows()
        {
            rowsContainer.Clear();

            rowsContainer.Add(BuildRow(
                NoneRowLabel,
                "Clears the tag. Untagged is an ordinary state - a one-off part on one character " +
                "needs no role.",
                true,
                string.Empty,
                () => onPick?.Invoke(0u)));

            string filterText = (filterField.value ?? string.Empty).Trim();
            bool anyTagMatched = false;

            int entryCount = registry != null && registry.entries != null ? registry.entries.Count : 0;
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                TargetTagEntry entry = registry.entries[entryIndex];
                if (entry == null || entry.stableId == 0u || string.IsNullOrEmpty(entry.name))
                {
                    continue;
                }
                if (!MatchesFilter(entry.name, filterText))
                {
                    continue;
                }

                anyTagMatched = true;
                uint pickedTagId = entry.stableId;
                rowsContainer.Add(BuildRow(
                    entry.name,
                    "id 0x" + entry.stableId.ToString("X8"),
                    true,
                    string.Empty,
                    () => onPick?.Invoke(pickedTagId)));
            }

            if (!anyTagMatched && filterText.Length > 0)
            {
                rowsContainer.Add(BuildCreateRow(filterText));
            }

            rowsContainer.Add(BuildRow(
                EditTagsRowLabel,
                "Opens the tag registry so a role can be added without leaving this rig.",
                true,
                string.Empty,
                OpenRegistryEditor));
        }

        /// <summary>
        /// Builds the optional "Create tag" row (spec §4.2.1). Still one typing surface — the filter
        /// text becomes the tag's name at the moment it is defined, not a second way to spell an
        /// existing one — which is why it only appears once nothing already matches.
        /// </summary>
        private VisualElement BuildCreateRow(string filterText)
        {
            // The filter list already guarantees no *substring* match exists, which rules out the
            // ordinary duplicate. This is a second, exact, case-insensitive check against the trimmed
            // name regardless, so a registry entry the filter loop skipped (empty name, id 0) can
            // never be "created over" and Jaw/jaw can never both exist - see IsNearDuplicateName's
            // remarks.
            if (IsNearDuplicateName(registry, filterText))
            {
                return BuildRow(
                    "'" + filterText + "' already exists",
                    "A tag with this name (ignoring case) already exists. Clear the filter to find " +
                    "it in the list above.",
                    false,
                    "Rename or reuse the existing tag instead of creating a near-duplicate.",
                    null);
            }

            return BuildRow(
                "Create tag '" + filterText + "'",
                "Defines a new tag named exactly this, in the project's target tag registry, and " +
                "assigns it immediately.",
                registry != null,
                "No target tag registry is assigned. Assign one to create tags from here.",
                () => CreateAndPick(filterText));
        }

        private void CreateAndPick(string tagName)
        {
            if (registry == null)
            {
                return;
            }

            Undo.RecordObject(registry, "Create Target Tag");
            if (registry.entries == null)
            {
                registry.entries = new List<TargetTagEntry>();
            }
            uint newTagId = registry.MintTagId();
            registry.entries.Add(new TargetTagEntry { name = tagName, stableId = newTagId });
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);

            onRegistryChanged?.Invoke();
            onPick?.Invoke(newTagId);
        }

        private void OpenRegistryEditor()
        {
            TargetTagQuickEditWindow.Open(registry, onRegistryChanged);
        }

        // -----------------------------------------------------------------------------------
        // Pure logic — extracted out of the row-building callbacks above so each is a plain,
        // testable predicate with no VisualElement, SerializedObject, or overlay machinery in the
        // way. Neither touches the asset database; both are exercised directly by
        // Tests/EditMode/TargetTagPickerLogicTests.cs.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// True when <paramref name="tagName"/> should be listed under the filter text
        /// <paramref name="filterText"/> (spec §4.2.1): a case-insensitive substring match, and an
        /// empty (or whitespace-only) filter matches everything.
        /// </summary>
        /// <param name="tagName">A tag's display name. Null is treated as empty.</param>
        /// <param name="filterText">The filter field's current text. Null is treated as empty.</param>
        /// <returns>True when the tag should be shown.</returns>
        public static bool MatchesFilter(string tagName, string filterText)
        {
            string trimmedFilter = filterText == null ? string.Empty : filterText.Trim();
            if (trimmedFilter.Length == 0)
            {
                return true;
            }
            string safeName = tagName ?? string.Empty;
            return safeName.IndexOf(trimmedFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// True when <paramref name="candidateName"/> matches an existing entry in
        /// <paramref name="registry"/>, ignoring case and leading/trailing whitespace — the guard
        /// that keeps "Create tag…" from ever minting a second, differently-cased spelling of a tag
        /// that already exists (spec §4.2.1: "it must reject near-duplicates case-insensitively").
        /// </summary>
        /// <param name="registry">The registry to check against. Null (or an empty entry list)
        /// never counts as a duplicate.</param>
        /// <param name="candidateName">The prospective new tag's name. Null is treated as empty.</param>
        /// <returns>True when an existing entry's name equals <paramref name="candidateName"/> case-
        /// and whitespace-insensitively.</returns>
        public static bool IsNearDuplicateName(TargetTagRegistry registry, string candidateName)
        {
            if (registry == null || registry.entries == null)
            {
                return false;
            }
            string trimmedCandidate = candidateName == null ? string.Empty : candidateName.Trim();
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                TargetTagEntry entry = registry.entries[entryIndex];
                if (entry == null)
                {
                    continue;
                }
                string trimmedEntryName = entry.name == null ? string.Empty : entry.name.Trim();
                if (string.Equals(trimmedEntryName, trimmedCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
