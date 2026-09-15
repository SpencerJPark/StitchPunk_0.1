// Copyright (c) 2026 Spencer Park. All rights reserved.

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
                Label hintLabel = new Label("Select an event on the left.");
                hintLabel.AddToClassList("toolkit-hint");
                bodyContainer.Add(hintLabel);
                return;
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            Label titleLabel = new Label(BoundEntry.name);
            titleLabel.AddToClassList("toolkit-pane-title");
            header.Add(titleLabel);
            bool isMaskable = AnimEventMaskKeys.IsMaskable(BoundEntry.eventKey);
            Label kindLabel = new Label(isMaskable
                ? "maskable · key " + BoundEntry.eventKey.ToString()
                : "pulse-only · key " + BoundEntry.eventKey.ToString());
            header.Add(kindLabel);
            bodyContainer.Add(header);

            TextField nameField = new TextField("Name") { isDelayed = true, value = BoundEntry.name };
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.name = changeEvent.newValue;
                PersistEntryEdit();
            });
            bodyContainer.Add(nameField);

            TextField descriptionField = new TextField("Description") { isDelayed = true, multiline = true };
            descriptionField.value = BoundEntry.description;
            descriptionField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.description = changeEvent.newValue;
                PersistEntryEdit();
            });
            bodyContainer.Add(descriptionField);

            IntegerField defaultWindowField = new IntegerField("Default window frames")
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
            bodyContainer.Add(defaultWindowField);

            TextField intParamLabelField = new TextField("Int param label") { isDelayed = true, value = BoundEntry.intParamLabel };
            intParamLabelField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.intParamLabel = changeEvent.newValue;
                PersistEntryEdit();
                RebuildPayloadPreviewSection();
            });
            bodyContainer.Add(intParamLabelField);

            TextField intValueNamesField = new TextField("Int value names") { isDelayed = true };
            intValueNamesField.value = BoundEntry.intParamValueNames != null
                ? string.Join(", ", BoundEntry.intParamValueNames)
                : string.Empty;
            intValueNamesField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.intParamValueNames = ParseCommaSeparatedNames(changeEvent.newValue);
                PersistEntryEdit();
                RebuildPayloadPreviewSection();
            });
            bodyContainer.Add(intValueNamesField);

            TextField floatParamLabelField = new TextField("Float param label") { isDelayed = true, value = BoundEntry.floatParamLabel };
            floatParamLabelField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.floatParamLabel = changeEvent.newValue;
                PersistEntryEdit();
                RebuildPayloadPreviewSection();
            });
            bodyContainer.Add(floatParamLabelField);

            TextField floatUnitField = new TextField("Float unit") { isDelayed = true, value = BoundEntry.floatParamUnit };
            floatUnitField.RegisterValueChangedCallback(changeEvent =>
            {
                BoundEntry.floatParamUnit = changeEvent.newValue;
                PersistEntryEdit();
            });
            bodyContainer.Add(floatUnitField);

            ObjectField previewClipField = new ObjectField("Preview clip")
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
            bodyContainer.Add(previewClipField);

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
