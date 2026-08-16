// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The custom inspector for <see cref="AnimEventKeyRegistry"/> (architecture section 7.1): the
    /// bound property fields, an "Add Event" button that never mints a colliding key, and the three
    /// problems a list of raw numbers hides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>These findings are not clip validation and deliberately do not live in
    /// <c>ClipValidation</c>.</strong> The registry is authoring furniture that is never baked, so a
    /// broken entry cannot reach a blob and has nothing to say to the clip validator. What a broken
    /// entry <em>can</em> do is point two names at one key, which makes them indistinguishable to
    /// every system downstream — so the check belongs where the mistake is made.
    /// </para>
    /// <para>
    /// UI Toolkit only, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>: this type
    /// overrides <see cref="UnityEditor.Editor.CreateInspectorGUI"/> and never the immediate-mode
    /// entry point.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(AnimEventKeyRegistry))]
    public sealed class AnimEventKeyRegistryEditor : UnityEditor.Editor
    {
        private static readonly Color WarningColor = new Color(0.92f, 0.72f, 0.32f);
        private static readonly Color CleanColor = new Color(0.45f, 0.78f, 0.48f);

        private VisualElement findingsContainer;

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = new VisualElement();

            root.Add(new PropertyField(serializedObject.FindProperty("referenceFrameRate")));
            root.Add(new PropertyField(serializedObject.FindProperty("entries")));

            Button addButton = new Button(AddEntry) { text = "Add Event" };
            addButton.style.marginTop = 6f;
            root.Add(addButton);

            findingsContainer = new VisualElement();
            findingsContainer.style.marginTop = 8f;
            root.Add(findingsContainer);

            RefreshFindings();

            // Picks up Undo/Redo and edits made from anywhere else, the same way the clip-set
            // roster does — without re-walking the list on every repaint.
            root.TrackSerializedObjectValue(serializedObject, changedObject => RefreshFindings());

            return root;
        }

        /// <summary>
        /// Appends an entry holding the lowest key nothing else claims, so the common case of
        /// "another event, please" cannot produce a duplicate or an unmaskable key by accident.
        /// </summary>
        private void AddEntry()
        {
            AnimEventKeyRegistry registry = (AnimEventKeyRegistry)target;

            Undo.RecordObject(registry, "Add Event Key");
            if (registry.entries == null)
            {
                registry.entries = new List<AnimEventKeyEntry>();
            }

            uint freeKey = registry.FindFirstFreeKey();
            registry.entries.Add(new AnimEventKeyEntry
            {
                name = "NewEvent",

                // Every maskable key is taken, so the new entry is necessarily pulse-only. It still
                // gets a unique key rather than a colliding one: a pulse-only event is a legitimate
                // thing to want, and silently duplicating a key would not be.
                eventKey = freeKey != 0u ? freeKey : NextFreeUnmaskableKey(registry)
            });

            EditorUtility.SetDirty(registry);
            serializedObject.Update();
            RefreshFindings();
        }

        private static uint NextFreeUnmaskableKey(AnimEventKeyRegistry registry)
        {
            uint candidate = AnimEventMaskKeys.LastMaskKey + 1u;
            while (registry.ContainsKey(candidate))
            {
                candidate++;
            }
            return candidate;
        }

        private void RefreshFindings()
        {
            if (findingsContainer == null)
            {
                return;
            }
            findingsContainer.Clear();

            AnimEventKeyRegistry registry = (AnimEventKeyRegistry)target;
            if (registry.entries == null || registry.entries.Count == 0)
            {
                findingsContainer.Add(MakeNote(
                    "No events yet. Add one, then assign this asset to a Clip Set to pick events "
                    + "by name in the Clip Editor.",
                    CleanColor));
                return;
            }

            int maskableCount = 0;
            HashSet<uint> seenKeys = new HashSet<uint>();
            List<string> problems = new List<string>();

            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = registry.entries[entryIndex];
                if (entry == null)
                {
                    problems.Add("Entry " + entryIndex + " is empty.");
                    continue;
                }

                string label = string.IsNullOrEmpty(entry.name)
                    ? "Entry " + entryIndex
                    : "'" + entry.name + "'";

                if (string.IsNullOrEmpty(entry.name))
                {
                    problems.Add(label + " has no name, so it shows as a bare number.");
                }

                if (entry.eventKey < (uint)ReservedEventKeys.FirstUserKey)
                {
                    problems.Add(
                        label + " uses key " + entry.eventKey + ", which is reserved by the "
                        + "package. Any clip using it fails validation rule V09.");
                }
                else if (!AnimEventMaskKeys.IsMaskable(entry.eventKey))
                {
                    problems.Add(
                        label + " uses key " + entry.eventKey + ", outside the maskable range "
                        + AnimEventMaskKeys.FirstMaskKey + "–" + AnimEventMaskKeys.LastMaskKey
                        + ". It is pulse-only and cannot hold a window.");
                }
                else
                {
                    maskableCount++;
                }

                if (!seenKeys.Add(entry.eventKey))
                {
                    problems.Add(
                        label + " repeats key " + entry.eventKey + ". Two names for one key are "
                        + "indistinguishable to every system that reads it.");
                }
            }

            findingsContainer.Add(MakeNote(
                "Maskable slots used: " + maskableCount + " / " + AnimEventMaskKeys.MaskKeyCount,
                problems.Count == 0 ? CleanColor : WarningColor));

            for (int problemIndex = 0; problemIndex < problems.Count; problemIndex++)
            {
                findingsContainer.Add(MakeNote(problems[problemIndex], WarningColor));
            }
        }

        private static Label MakeNote(string text, Color textColor)
        {
            Label note = new Label(text);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.color = textColor;
            note.style.marginTop = 2f;
            return note;
        }
    }
}
