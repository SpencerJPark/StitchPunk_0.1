// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's middle column: one event's fields, payload schema, preview clip and where it is used.</summary>
    public sealed class EventKeyInspectorColumn : VisualElement
    {
        public AnimEventKeyRegistry Registry { get; private set; }

        public AnimEventKeyEntry BoundEntry { get; private set; }

        private readonly VisualElement bodyContainer;
        private VisualElement payloadPreviewSection;
        private VisualElement usageSection;

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
            usageSection = null;
            RebuildBody();
        }

        private void RebuildBody()
        {
            bodyContainer.Clear();

            if (BoundEntry == null)
            {
                Label hintLabel = new Label("Select an event on the left.");
                hintLabel.AddToClassList("clip-editor__hint");
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

            usageSection = new VisualElement { name = "events-inspector-usage" };
            bodyContainer.Add(usageSection);
            RefreshUsage();
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

        public void RefreshUsage()
        {
            if (usageSection == null)
            {
                return;
            }

            usageSection.Clear();

            if (BoundEntry == null)
            {
                return;
            }

            List<AssetReference> references = AssetReferenceIndex.ReferencesToEventKey(BoundEntry.eventKey);
            if (references == null || references.Count == 0)
            {
                Label emptyLabel = new Label("Nothing uses this event yet.");
                emptyLabel.AddToClassList("clip-editor__hint");
                usageSection.Add(emptyLabel);
                return;
            }

            Dictionary<UnityEngine.Object, List<string>> clipOwnerDetails = new Dictionary<UnityEngine.Object, List<string>>();
            Dictionary<UnityEngine.Object, List<string>> cutsceneOwnerDetails = new Dictionary<UnityEngine.Object, List<string>>();
            Dictionary<UnityEngine.Object, List<string>> profileOwnerDetails = new Dictionary<UnityEngine.Object, List<string>>();
            Dictionary<UnityEngine.Object, List<string>> otherOwnerDetails = new Dictionary<UnityEngine.Object, List<string>>();

            for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                AssetReference reference = references[referenceIndex];
                Dictionary<UnityEngine.Object, List<string>> targetGroup;
                switch (reference.kind)
                {
                    case AssetReferenceKind.ClipEventMarker:
                        targetGroup = clipOwnerDetails;
                        break;
                    case AssetReferenceKind.CutsceneEventMarker:
                        targetGroup = cutsceneOwnerDetails;
                        break;
                    case AssetReferenceKind.ProfileRagdollEvent:
                        targetGroup = profileOwnerDetails;
                        break;
                    default:
                        targetGroup = otherOwnerDetails;
                        break;
                }

                if (!targetGroup.TryGetValue(reference.owner, out List<string> detailList))
                {
                    detailList = new List<string>();
                    targetGroup[reference.owner] = detailList;
                }
                if (!string.IsNullOrEmpty(reference.detail))
                {
                    detailList.Add(reference.detail);
                }
            }

            Label headlineLabel = new Label(string.Format(
                "Used by {0} clips · {1} cutscenes · {2} profiles",
                clipOwnerDetails.Count,
                cutsceneOwnerDetails.Count,
                profileOwnerDetails.Count));
            usageSection.Add(headlineLabel);

            AddUsageGroup(usageSection, "Clip markers", clipOwnerDetails);
            AddUsageGroup(usageSection, "Cutscene markers", cutsceneOwnerDetails);
            AddUsageGroup(usageSection, "Profile ragdoll events", profileOwnerDetails);
            AddUsageGroup(usageSection, "Other", otherOwnerDetails);
        }

        private static void AddUsageGroup(
            VisualElement container, string headerText, Dictionary<UnityEngine.Object, List<string>> ownerDetails)
        {
            if (ownerDetails.Count == 0)
            {
                return;
            }

            Label groupHeaderLabel = new Label(headerText);
            groupHeaderLabel.AddToClassList("clip-editor__hint");
            container.Add(groupHeaderLabel);

            foreach (KeyValuePair<UnityEngine.Object, List<string>> ownerEntry in ownerDetails)
            {
                UnityEngine.Object owner = ownerEntry.Key;
                List<string> details = ownerEntry.Value;
                int markerCount = details.Count > 0 ? details.Count : 1;
                string ownerName = owner != null ? owner.name : "(missing)";
                string buttonText = markerCount == 1
                    ? ownerName
                    : string.Format("{0} ({1} markers)", ownerName, markerCount);

                Button ownerButton = new Button(() => EditorGUIUtility.PingObject(owner)) { text = buttonText };
                if (details.Count > 0)
                {
                    ownerButton.tooltip = string.Join("\n", details);
                }
                container.Add(ownerButton);
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
