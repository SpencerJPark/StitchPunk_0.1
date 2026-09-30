// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's middle column: one event's fields, payload schema and preview clip.</summary>
    public sealed class EventKeyInspectorColumn : VisualElement
    {
        public AnimEventKeyRegistry Registry { get; private set; }

        public AnimEventKeyEntry BoundEntry { get; private set; }

        /// <summary>Raised by the empty state's action button; this column cannot create a key itself.</summary>
        public event Action NewEventKeyRequested;

        private readonly VisualElement bodyContainer;
        private VisualElement payloadPreviewSection;

        public EventKeyInspectorColumn()
        {
            name = "events-inspector-column";
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;
            style.flexGrow = 1f;

            bodyContainer = new VisualElement { name = "events-inspector-body" };
            bodyContainer.style.flexGrow = 1f;
            Add(bodyContainer);
        }

        public void Bind(AnimEventKeyRegistry registry, AnimEventKeyEntry entry)
        {
            Registry = registry;
            BoundEntry = entry;
            payloadPreviewSection = null;
            RebuildBody();
        }

        private void RebuildBody()
        {
            bodyContainer.Clear();

            if (BoundEntry == null)
            {
                bodyContainer.Add(ToolkitChrome.MakeEmptyState(
                    "events-inspector-empty",
                    "No event selected",
                    "Pick a key on the left, or create one.",
                    "New event key",
                    () => NewEventKeyRequested?.Invoke()));
                return;
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            Label titleLabel = new Label(BoundEntry.name);
            titleLabel.AddToClassList("toolkit-pane-title");
            header.Add(titleLabel);
            bool isMaskable = AnimEventMaskKeys.IsMaskable(BoundEntry.eventKey);
            string maskability = isMaskable ? "maskable" : "pulse-only";
            Label kindBadge = ToolkitChrome.MakeBadge(BoundEntry.eventKey.ToString() + " · " + maskability, ToolkitStatusTone.Neutral);
            kindBadge.tooltip = "event key " + BoundEntry.eventKey.ToString() + ", " + maskability;
            header.Add(kindBadge);
            bodyContainer.Add(header);

            TextField nameField = new TextField() { isDelayed = true, value = BoundEntry.name };
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.name = changeEvent.newValue;
                PersistEntryEdit();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Name", nameField, "Display name of this event key"));

            TextField descriptionField = new TextField() { isDelayed = true, multiline = true };
            descriptionField.value = BoundEntry.description;
            descriptionField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.description = changeEvent.newValue;
                PersistEntryEdit();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Description", descriptionField, "What this event key is for"));

            IntegerField defaultWindowField = new IntegerField()
            {
                isDelayed = true,
                value = BoundEntry.defaultWindowFrames
            };
            defaultWindowField.RegisterValueChangedCallback(changeEvent =>
            {
                int clampedValue = Mathf.Max(0, changeEvent.newValue);
                BoundEntry.defaultWindowFrames = clampedValue;
                if (clampedValue != changeEvent.newValue)
                {
                    defaultWindowField.SetValueWithoutNotify(clampedValue);
                }
                PersistEntryEdit();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Window frames", defaultWindowField,
                "Default window length, in frames, when this key is placed as a window"));

            TextField intParamLabelField = new TextField() { isDelayed = true, value = BoundEntry.intParamLabel };
            intParamLabelField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.intParamLabel = changeEvent.newValue;
                PersistEntryEdit();
                RebuildPayloadPreviewSection();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Int param label", intParamLabelField, "What the int param means for this key"));

            TextField intValueNamesField = new TextField() { isDelayed = true };
            intValueNamesField.value = BoundEntry.intParamValueNames != null
                ? string.Join(", ", BoundEntry.intParamValueNames)
                : string.Empty;
            intValueNamesField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.intParamValueNames = ParseCommaSeparatedNames(changeEvent.newValue);
                PersistEntryEdit();
                RebuildPayloadPreviewSection();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Int value names", intValueNamesField, "Comma-separated names, one per int param value"));
            bodyContainer.Add(ToolkitChrome.MakeHint("One key per kind of event: the int param picks the variant. For sounds, one Sound key and name each sound here."));

            TextField floatParamLabelField = new TextField() { isDelayed = true, value = BoundEntry.floatParamLabel };
            floatParamLabelField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.floatParamLabel = changeEvent.newValue;
                PersistEntryEdit();
                RebuildPayloadPreviewSection();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Float param label", floatParamLabelField, "What the float param means for this key"));

            TextField floatUnitField = new TextField() { isDelayed = true, value = BoundEntry.floatParamUnit };
            floatUnitField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.floatParamUnit = changeEvent.newValue;
                PersistEntryEdit();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Float unit", floatUnitField, "Unit shown after the float param value"));

            ObjectField previewClipField = new ObjectField()
            {
                objectType = typeof(AudioClip),
                allowSceneObjects = false,
                value = BoundEntry.previewClip
            };
            previewClipField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.previewClip = changeEvent.newValue as AudioClip;
                PersistEntryEdit();
            });
            bodyContainer.Add(ToolkitChrome.MakePropertyRow("Preview clip", previewClipField, "Audio clip played when previewing this key"));

            payloadPreviewSection = new VisualElement { name = "events-inspector-payload-preview" };
            bodyContainer.Add(payloadPreviewSection);
            RebuildPayloadPreviewSection();
        }

        private void RebuildPayloadPreviewSection()
        {
            if (payloadPreviewSection == null)
            {
                return;
            }

            payloadPreviewSection.Clear();

            if (BoundEntry == null || !EventPayloadFieldBuilder.HasPayloadSchema(BoundEntry))
            {
                return;
            }

            Label sectionLabel = new Label("Marker preview");
            sectionLabel.AddToClassList("toolkit-pane-title");
            payloadPreviewSection.Add(sectionLabel);

            VisualElement intPreviewField = EventPayloadFieldBuilder.BuildIntField(BoundEntry, 0, value => { });
            if (intPreviewField != null)
            {
                payloadPreviewSection.Add(intPreviewField);
            }

            VisualElement floatPreviewField = EventPayloadFieldBuilder.BuildFloatField(BoundEntry, 0f, value => { });
            if (floatPreviewField != null)
            {
                payloadPreviewSection.Add(floatPreviewField);
            }
        }

        private static List<string> ParseCommaSeparatedNames(string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue))
            {
                return new List<string>();
            }

            string[] splitNames = rawValue.Split(',');
            List<string> trimmedNames = new List<string>();
            for (int nameIndex = 0; nameIndex < splitNames.Length; nameIndex++)
            {
                string trimmedName = splitNames[nameIndex].Trim();
                if (trimmedName.Length > 0)
                {
                    trimmedNames.Add(trimmedName);
                }
            }
            return trimmedNames;
        }

        private void PersistEntryEdit()
        {
            if (Registry == null)
            {
                return;
            }
            VocabularyRegistryProvider.Persist(Registry);
        }
    }
}
