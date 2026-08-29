// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The fixed text and per-row description this picker shows for one <see cref="IVocabularyRegistry"/>
    /// — everything that differs between a target-tag picker and an event-name picker (amendment E6
    /// Task 3: "one shared searchable picker serves both vocabularies").
    /// </summary>
    public readonly struct VocabularyPickerConfig
    {
        /// <summary>
        /// Label for the always-first "clear the binding" row, or null/empty to omit that row
        /// entirely. Tags offer this (untagged is an ordinary state, spec §4.2); an event marker
        /// always fires <em>some</em> event, so the event picker passes null here.
        /// </summary>
        public readonly string NoneRowLabel;
        public readonly string NoneRowDescription;

        /// <summary>Singular noun used in "Create &lt;noun&gt; '&lt;text&gt;'", e.g. "tag" or "event".</summary>
        public readonly string CreateRowNoun;

        /// <summary>
        /// Short label for the button pinned beside the search field, e.g. "Edit…". Kept short
        /// deliberately — <see cref="EditRowLabel"/> carries the longer phrasing as the button's
        /// tooltip instead, because "Edit tags…" does not fit next to a search field.
        /// </summary>
        public readonly string EditButtonLabel;

        /// <summary>Longer phrasing shown only in the edit button's tooltip, e.g. "Edit tags…".</summary>
        public readonly string EditRowLabel;
        public readonly string EditRowDescription;

        /// <summary>Title given to the <see cref="VocabularyQuickEditWindow"/> the edit row opens.</summary>
        public readonly string QuickEditWindowTitle;

        /// <summary>Message shown in the quick-edit window when no registry object is available yet.</summary>
        public readonly string QuickEditMissingMessage;

        /// <summary>
        /// Builds the grey body of a row's hover card from that row's id — the event's authored
        /// description where the vocabulary has one, and otherwise whatever the id itself can say
        /// (e.g. "id 0x1A2B3C4D").
        /// </summary>
        public readonly Func<uint, string> DescribeEntryId;

        public VocabularyPickerConfig(
            string noneRowLabel,
            string noneRowDescription,
            string createRowNoun,
            string editButtonLabel,
            string editRowLabel,
            string editRowDescription,
            string quickEditWindowTitle,
            string quickEditMissingMessage,
            Func<uint, string> describeEntryId)
        {
            NoneRowLabel = noneRowLabel;
            NoneRowDescription = noneRowDescription;
            CreateRowNoun = createRowNoun;
            EditButtonLabel = editButtonLabel;
            EditRowLabel = editRowLabel;
            EditRowDescription = editRowDescription;
            QuickEditWindowTitle = quickEditWindowTitle;
            QuickEditMissingMessage = quickEditMissingMessage;
            DescribeEntryId = describeEntryId;
        }

        /// <summary>
        /// The target-tag flavour of this config, in one place rather than at each call site.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two surfaces open a tag picker — the rig inspector's tag column and the Clip Editor's
        /// track-binding button — and every string they show has to match, or the same control reads
        /// as two different features depending on where it was opened from. A shared factory is what
        /// makes that structural rather than a thing two files must remember to keep in step.
        /// </para>
        /// <para>
        /// <strong>The "(none)" row exists here and does not for events.</strong> Untagged is an
        /// ordinary state for a target (spec §4.2), while an event marker always fires
        /// <em>some</em> event, so there is nothing for an event picker to clear to.
        /// </para>
        /// </remarks>
        public static VocabularyPickerConfig ForTargetTags(TargetTagRegistry registry)
        {
            return new VocabularyPickerConfig(
                "(none)",
                "Leave this target untagged. An untagged target is animated by target id only, "
                    + "so its clips do not travel to another rig.",
                "tag",
                "Edit…",
                "Edit tags…",
                "Rename, add or remove the project's target tags.",
                "Target Tags",
                "No tag registry is available yet.",
                tagId =>
                {
                    // Never the raw number where a name exists (spec §4.2.3). The hex form is the
                    // one permitted exception: a dangling id after its tag was deleted has no name
                    // left to show, and the number is what makes that row findable again.
                    string resolvedName = registry != null ? registry.FindName(tagId) : null;
                    return resolvedName ?? "(unresolved 0x" + tagId.ToString("X8") + ")";
                });
        }

        /// <summary>
        /// The tag picker a keyed track's binding opens (amendment A56 D5). Same strings as
        /// <see cref="ForTargetTags"/> but no "(none)" row: a row's keys are stored against its tag,
        /// so a keyed track has nothing legal to clear to — only a rig <em>part</em> may be untagged.
        /// </summary>
        public static VocabularyPickerConfig ForTrackTagRebind(TargetTagRegistry registry)
        {
            VocabularyPickerConfig partConfig = ForTargetTags(registry);
            return new VocabularyPickerConfig(
                null,
                null,
                partConfig.CreateRowNoun,
                partConfig.EditButtonLabel,
                partConfig.EditRowLabel,
                partConfig.EditRowDescription,
                partConfig.QuickEditWindowTitle,
                partConfig.QuickEditMissingMessage,
                partConfig.DescribeEntryId);
        }

        /// <summary>
        /// The event-name flavour of this config (E6 Task 4). No "(none)" row: unlike a rig target,
        /// an event marker always fires <em>some</em> event, so there is nothing to clear a binding
        /// to.
        /// </summary>
        /// <remarks>
        /// An event's hover text is its own authored description
        /// (<see cref="AnimEventKeyEntry.description"/>) rather than its name, which the row above
        /// the card already shows — that note is written in the registry precisely so the person
        /// choosing an event can read what it does before picking it. Target tags carry no such
        /// field, so they keep describing themselves by name.
        /// </remarks>
        public static VocabularyPickerConfig ForEventKeys(AnimEventKeyRegistry registry)
        {
            return new VocabularyPickerConfig(
                null,
                null,
                "event",
                "Edit…",
                "Edit events…",
                "Rename, add or remove the project's event names.",
                "Events",
                "No event registry is available yet.",
                eventKey =>
                {
                    string authoredDescription =
                        registry != null ? registry.FindDescription(eventKey) : null;
                    if (!string.IsNullOrEmpty(authoredDescription))
                    {
                        return authoredDescription;
                    }

                    // Nothing written yet: the key is the next most useful thing to say about the
                    // event, being the number every marker using it actually bakes.
                    return "Key " + eventKey.ToString()
                        + " — no description written for this event yet.";
                });
        }
    }

    /// <summary>
    /// The searchable vocabulary picker (Phase E target-tags spec §4.2.1, generalised in amendment
    /// E6 Task 3): the one control that may ever choose an id out of a project vocabulary — a
    /// <see cref="TargetTagRegistry"/> tag or an <see cref="AnimEventKeyRegistry"/> event name — for
    /// a rig target's tag, a track's tag binding, or an event marker's key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Selection only, never typing — this is the whole safety argument for spec §6.1's
    /// lenient T2.</strong> A name is typed exactly once, in the registry, when the row is defined
    /// (spec §4.2.1). Every other surface offers the existing rows and nothing else, so a wrong pick
    /// is a wrong <em>selection</em> from a visible list — a mistake you can see — rather than a typo
    /// that resolves to nothing and gets skipped with a warning nobody reads.
    /// </para>
    /// <para>
    /// <strong>One picker for both vocabularies, not two.</strong> Originally built as
    /// <c>TargetTagPicker</c> for target tags alone (E1.5), this class was generalised for amendment
    /// E6 Task 3 rather than duplicated for event names: the row content, filtering, "(none)"
    /// handling, the "Create…" row, and the pinned Edit button are identical in shape between a
    /// tag and an event, differing only in the strings in <see cref="VocabularyPickerConfig"/> and which
    /// <see cref="IVocabularyRegistry"/> is asked. <see cref="Open"/> takes both an
    /// <see cref="IVocabularyRegistry"/> (the id lookups and minting) and the underlying
    /// <see cref="ScriptableObject"/> (needed only to hand to <see cref="VocabularyQuickEditWindow"/>,
    /// which hosts a real <see cref="UnityEditor.Editor"/> for it).
    /// </para>
    /// <para>
    /// <strong>Built on <see cref="PickerOverlay"/>, the base <c>ClipComponentPicker</c> was
    /// generalised into.</strong> The overlay chrome — dismiss-on-outside-press, Escape, the hover
    /// card, panel placement and its overhang clamp — is identical between every picker in this
    /// package; only the row content and the filter field are new here.
    /// </para>
    /// </remarks>
    public sealed class VocabularyPicker : PickerOverlay
    {
        private const float PanelWidth = 260f;
        private const float CardWidth = 240f;

        /// <summary>Caps the row list so a long vocabulary scrolls instead of growing the panel
        /// past the window; the search field and Edit button above it stay pinned in view.</summary>
        private const float RowsMaxHeight = 240f;

        private readonly IVocabularyRegistry registry;
        private readonly ScriptableObject registryObject;
        private readonly VocabularyPickerConfig config;
        private readonly Action<uint> onPick;
        private readonly Action onRegistryChanged;
        private readonly TextField filterField;
        private readonly VisualElement rowsContainer;

        private VocabularyPicker(
            IVocabularyRegistry registry,
            ScriptableObject registryObject,
            VocabularyPickerConfig config,
            Action<uint> onPick,
            Action onRegistryChanged)
            : base(PanelWidth, CardWidth)
        {
            this.registry = registry;
            this.registryObject = registryObject;
            this.config = config;
            this.onPick = onPick;
            this.onRegistryChanged = onRegistryChanged;

            // Search field and Edit button share a row, Edit fixed at its right edge, so the edit
            // affordance stays one place to look regardless of how many rows the list holds below
            // it — the owner's complaint with the old trailing "Edit tags…" row (spec §4.2.2).
            VisualElement searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.alignItems = Align.Center;
            listPanel.Add(searchRow);

            filterField = new TextField();
            filterField.style.flexGrow = 1f;
            filterField.style.marginLeft = 4f;
            filterField.style.marginRight = 4f;
            filterField.style.marginTop = 2f;
            filterField.style.marginBottom = 2f;
            // Filtering only ever narrows the list; it never binds anything by itself (spec §4.2.1).
            filterField.RegisterValueChangedCallback(changeEvent => RefreshRows());
            searchRow.Add(filterField);

            Button editButton = new Button(OpenQuickEditWindow);
            editButton.text = config.EditButtonLabel;
            editButton.style.flexShrink = 0f;
            editButton.style.marginRight = 4f;
            editButton.style.marginTop = 2f;
            editButton.style.marginBottom = 2f;
            // A plain Button gets no hover card of its own (PickerOverlay.BuildRow is what wires
            // those), so the tooltip is carrying the full explanation the old row label used to.
            editButton.tooltip = config.EditRowLabel + " " + config.EditRowDescription;
            searchRow.Add(editButton);

            ScrollView rowsScrollView = new ScrollView(ScrollViewMode.Vertical);
            rowsScrollView.style.maxHeight = RowsMaxHeight;
            listPanel.Add(rowsScrollView);

            rowsContainer = new VisualElement();
            rowsScrollView.Add(rowsContainer);

            RefreshRows();

            // Amendment A54: a still-open picker's row list stays current while a separate
            // VocabularyQuickEditWindow (or the Project Settings page) edits the same registry —
            // including an add or remove, which has no field to bubble a FocusOutEvent from and so
            // could not be caught any other way. Unsubscribes on DetachFromPanelEvent rather than in
            // Close() so this stays self-contained without PickerOverlay needing to know about it;
            // Close() always ends in RemoveFromHierarchy(), which is what raises that event.
            VocabularyRegistryProvider.RegistryChanged += RefreshRows;
            RegisterCallback<DetachFromPanelEvent>(
                detachFromPanelEvent => VocabularyRegistryProvider.RegistryChanged -= RefreshRows);
        }

        /// <summary>Opens the picker over <paramref name="host"/>, hung under <paramref name="anchor"/>.</summary>
        /// <param name="host">The element the overlay covers and is placed within.</param>
        /// <param name="anchor">The control that opened it; the panel hangs from its lower-left.</param>
        /// <param name="registry">
        /// The vocabulary to pick from. A null or empty registry still opens — the list is then just
        /// "(none)" (when configured) and "Create…", with the Edit button beside the search field
        /// always available — because a project with no rows yet is exactly when a person most needs
        /// the in-flow create-and-edit path.
        /// </param>
        /// <param name="registryObject">
        /// The same registry as a <see cref="ScriptableObject"/>, so the Edit button can hand it to
        /// <see cref="VocabularyQuickEditWindow"/>. May be null alongside a null
        /// <paramref name="registry"/>.
        /// </param>
        /// <param name="config">The strings and per-row hover text this vocabulary uses.</param>
        /// <param name="onPick">
        /// Invoked with the chosen row's id, or 0 for "(none)". Not invoked when the picker is
        /// dismissed without a choice, or when the Edit button is pressed.
        /// </param>
        /// <param name="onRegistryChanged">
        /// Invoked after a row is minted through "Create…", or after the quick-edit window closes —
        /// either can change what a caller's own cached names should show.
        /// </param>
        public static VocabularyPicker Open(
            VisualElement host,
            VisualElement anchor,
            IVocabularyRegistry registry,
            ScriptableObject registryObject,
            VocabularyPickerConfig config,
            Action<uint> onPick,
            Action onRegistryChanged)
        {
            if (host == null)
            {
                return null;
            }
            VocabularyPicker picker =
                new VocabularyPicker(registry, registryObject, config, onPick, onRegistryChanged);
            picker.FinalizeOpen(host, anchor);
            return picker;
        }

        private void RefreshRows()
        {
            rowsContainer.Clear();

            if (!string.IsNullOrEmpty(config.NoneRowLabel))
            {
                rowsContainer.Add(BuildRow(
                    config.NoneRowLabel,
                    config.NoneRowDescription,
                    true,
                    string.Empty,
                    () => onPick?.Invoke(0u)));
            }

            string filterText = (filterField.value ?? string.Empty).Trim();
            bool anyEntryMatched = false;

            int entryCount = registry != null ? registry.VocabularyEntryCount : 0;
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                string entryName = registry.VocabularyEntryName(entryIndex);
                uint entryId = registry.VocabularyEntryId(entryIndex);
                if (entryId == 0u || string.IsNullOrEmpty(entryName))
                {
                    continue;
                }
                if (!MatchesFilter(entryName, filterText))
                {
                    continue;
                }

                anyEntryMatched = true;
                uint pickedId = entryId;
                rowsContainer.Add(BuildRow(
                    entryName,
                    config.DescribeEntryId != null ? config.DescribeEntryId(entryId) : string.Empty,
                    true,
                    string.Empty,
                    () => onPick?.Invoke(pickedId)));
            }

            if (!anyEntryMatched && filterText.Length > 0)
            {
                rowsContainer.Add(BuildCreateRow(filterText));
            }
        }

        /// <summary>
        /// Builds the optional "Create…" row (spec §4.2.1). Still one typing surface — the filter
        /// text becomes the row's name at the moment it is defined, not a second way to spell an
        /// existing one — which is why it only appears once nothing already matches.
        /// </summary>
        private VisualElement BuildCreateRow(string filterText)
        {
            // The filter list already guarantees no *substring* match exists, which rules out the
            // ordinary duplicate. This is a second, exact, case-insensitive check against the trimmed
            // name regardless, so a registry entry the filter loop skipped (empty name, id 0) can
            // never be "created over" — see IsNearDuplicateName's remarks.
            if (IsNearDuplicateName(registry, filterText))
            {
                return BuildRow(
                    "'" + filterText + "' already exists",
                    "A " + config.CreateRowNoun + " with this name (ignoring case) already exists. " +
                    "Clear the filter to find it in the list above.",
                    false,
                    "Rename or reuse the existing " + config.CreateRowNoun + " instead of creating a " +
                    "near-duplicate.",
                    null);
            }

            return BuildRow(
                "Create " + config.CreateRowNoun + " '" + filterText + "'",
                "Defines a new " + config.CreateRowNoun + " named exactly this, in the project's " +
                "registry, and assigns it immediately.",
                registry != null,
                "No registry is available to create a " + config.CreateRowNoun + " in.",
                () => CreateAndPick(filterText));
        }

        private void CreateAndPick(string entryName)
        {
            if (registry == null)
            {
                return;
            }

            uint newId = registry.CreateVocabularyEntry(entryName);
            VocabularyRegistryProvider.PersistVocabulary(registryObject);
            onRegistryChanged?.Invoke();
            onPick?.Invoke(newId);
        }

        /// <summary>
        /// Opens the registry's quick-edit window. The picker stays open behind it now that Edit is a
        /// pinned button rather than a self-closing row; its own row list stays current regardless via
        /// the <see cref="VocabularyRegistryProvider.RegistryChanged"/> subscription taken out in the
        /// constructor, so this only has to worry about the outer caller's own refresh once the
        /// quick-edit window actually closes.
        /// </summary>
        private void OpenQuickEditWindow()
        {
            VocabularyQuickEditWindow.Open(
                config.QuickEditWindowTitle,
                registryObject,
                config.QuickEditMissingMessage,
                onClosed: onRegistryChanged);
        }

        // -----------------------------------------------------------------------------------
        // Pure logic — extracted out of the row-building callbacks above so each is a plain,
        // testable predicate with no VisualElement, SerializedObject, or overlay machinery in the
        // way. Neither touches the asset database; both are exercised directly by
        // Tests/EditMode/VocabularyPickerLogicTests.cs.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// True when <paramref name="entryName"/> should be listed under the filter text
        /// <paramref name="filterText"/> (spec §4.2.1): a case-insensitive substring match, and an
        /// empty (or whitespace-only) filter matches everything.
        /// </summary>
        /// <param name="entryName">A row's display name. Null is treated as empty.</param>
        /// <param name="filterText">The filter field's current text. Null is treated as empty.</param>
        /// <returns>True when the row should be shown.</returns>
        public static bool MatchesFilter(string entryName, string filterText)
        {
            string trimmedFilter = filterText == null ? string.Empty : filterText.Trim();
            if (trimmedFilter.Length == 0)
            {
                return true;
            }
            string safeName = entryName ?? string.Empty;
            return safeName.IndexOf(trimmedFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// True when <paramref name="candidateName"/> matches an existing name in
        /// <paramref name="existingNames"/>, ignoring case and leading/trailing whitespace — the
        /// guard that keeps "Create…" from ever minting a second, differently-cased spelling of a
        /// row that already exists (spec §4.2.1: "it must reject near-duplicates case-insensitively").
        /// </summary>
        /// <param name="existingNames">Every row's current name. Null or empty never counts as a
        /// duplicate. A null entry in the list is treated as empty.</param>
        /// <param name="candidateName">The prospective new row's name. Null is treated as empty.</param>
        /// <returns>True when an existing name equals <paramref name="candidateName"/> case- and
        /// whitespace-insensitively.</returns>
        public static bool IsNearDuplicateName(IReadOnlyList<string> existingNames, string candidateName)
        {
            if (existingNames == null)
            {
                return false;
            }
            string trimmedCandidate = candidateName == null ? string.Empty : candidateName.Trim();
            for (int nameIndex = 0; nameIndex < existingNames.Count; nameIndex++)
            {
                string trimmedExistingName =
                    existingNames[nameIndex] == null ? string.Empty : existingNames[nameIndex].Trim();
                if (string.Equals(trimmedExistingName, trimmedCandidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Convenience overload production code uses directly against a live registry.</summary>
        /// <param name="registry">The registry to check against. Null never counts as a duplicate.</param>
        /// <param name="candidateName">The prospective new row's name.</param>
        public static bool IsNearDuplicateName(IVocabularyRegistry registry, string candidateName)
        {
            if (registry == null)
            {
                return false;
            }
            int entryCount = registry.VocabularyEntryCount;
            List<string> existingNames = new List<string>(entryCount);
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                existingNames.Add(registry.VocabularyEntryName(entryIndex));
            }
            return IsNearDuplicateName(existingNames, candidateName);
        }
    }
}
