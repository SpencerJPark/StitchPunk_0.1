// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One inspector for an event marker on either a clip or a cutscene, driven entirely
    /// through <see cref="IEventMarkerAccessor"/> so both hosts share the same rows.</summary>
    public sealed class EventMarkerInspectorElement : VisualElement
    {
        private readonly VisualElement rowsContainer;
        private readonly VisualElement findingsContainer;

        private IEventMarkerAccessor boundAccessor;
        private AnimEventKeyRegistry boundRegistry;

        public VisualElement PickerHost { get; set; }

        public Func<uint, VisualElement, bool> PayloadOverride { get; set; }

        public event Action<EventMarkerField> FieldEdited;

        public event Action RegistryChanged;

        public EventMarkerInspectorElement()
        {
            this.rowsContainer = new VisualElement();
            this.findingsContainer = new VisualElement();
            this.Add(this.rowsContainer);
            this.Add(this.findingsContainer);
        }

        public void Bind(IEventMarkerAccessor accessor, AnimEventKeyRegistry registry)
        {
            this.boundAccessor = accessor;
            this.boundRegistry = registry;
            this.rowsContainer.Clear();

            if (accessor == null || !accessor.MarkerExists)
            {
                return;
            }

            float frameRate = ResolveFrameRate(registry);
            this.rowsContainer.Add(this.BuildKeyRow(accessor, registry));
            this.rowsContainer.Add(this.BuildTimeRow(accessor, frameRate));
            this.rowsContainer.Add(this.BuildPayloadRow(accessor, registry));

            if (accessor.HasWindow)
            {
                this.rowsContainer.Add(this.BuildWindowRow(accessor, frameRate));
            }

            if (accessor.HasSkipFlag)
            {
                this.rowsContainer.Add(this.BuildFireOnSkipRow(accessor));
            }

            if (accessor.HasHoldFlag)
            {
                this.rowsContainer.Add(this.BuildHoldRow(accessor));
            }
        }

        public void SetFindings(IReadOnlyList<ValidationMessage> findings)
        {
            this.findingsContainer.Clear();
            if (findings == null)
            {
                return;
            }

            for (int findingIndex = 0; findingIndex < findings.Count; findingIndex++)
            {
                ValidationMessage message = findings[findingIndex];
                Label findingLabel = new Label(message.code.ToString() + " · " + message.text);
                findingLabel.style.whiteSpace = WhiteSpace.Normal;
                findingLabel.style.color = message.severity == ValidationSeverity.Error
                    ? ToolkitPalette.Error
                    : ToolkitPalette.Warning;
                this.findingsContainer.Add(findingLabel);
            }
        }

        private VisualElement BuildKeyRow(IEventMarkerAccessor accessor, AnimEventKeyRegistry registry)
        {
            VisualElement keyRow = new VisualElement();
            uint currentKey = accessor.Key;
            string keyName = ResolveKeyName(registry, currentKey);

            Button keyButton = new Button(() => this.OpenKeyPicker(accessor, registry));
            keyButton.text = "Event: " + keyName;
            keyRow.Add(keyButton);

            Label hintLabel = new Label(BuildKeyHintText(accessor, currentKey, keyName));
            ApplyHintStyle(hintLabel);
            keyRow.Add(hintLabel);

            return keyRow;
        }

        private void OpenKeyPicker(IEventMarkerAccessor accessor, AnimEventKeyRegistry registry)
        {
            VisualElement pickerHost = this.PickerHost ?? this.panel?.visualTree;
            if (pickerHost == null)
            {
                return;
            }

            VocabularyPicker.Open(
                pickerHost,
                this,
                registry,
                registry,
                VocabularyPickerConfig.ForEventKeys(registry),
                chosenKey => this.OnKeyPicked(accessor, registry, chosenKey),
                () => this.RegistryChanged?.Invoke());
        }

        private void OnKeyPicked(IEventMarkerAccessor accessor, AnimEventKeyRegistry registry, uint chosenKey)
        {
            accessor.Key = chosenKey;

            AnimEventKeyEntry chosenEntry = FindEntry(registry, chosenKey);
            // A registry-side default window only backfills a marker that has never had one of its own.
            if (accessor.HasWindow && accessor.WindowSeconds <= 0f
                && chosenEntry != null && chosenEntry.defaultWindowFrames > 0)
            {
                float frameRate = ResolveFrameRate(registry);
                accessor.WindowSeconds = chosenEntry.defaultWindowFrames / frameRate;
            }

            this.FieldEdited?.Invoke(EventMarkerField.Key);
        }

        private static string BuildKeyHintText(IEventMarkerAccessor accessor, uint currentKey, string keyName)
        {
            if (currentKey < (uint)ReservedEventKeys.FirstUserKey)
            {
                return keyName + " is reserved by the package — this marker will fail validation (V09).";
            }

            if (accessor.HasWindow && !AnimEventMaskKeys.IsMaskable(currentKey))
            {
                return keyName + " · pulse-only (outside the maskable range, so a window here would never open).";
            }

            if (accessor.HasWindow)
            {
                return keyName + " · mask bit " + (currentKey - AnimEventMaskKeys.FirstMaskKey) + ".";
            }

            return keyName;
        }

        private VisualElement BuildTimeRow(IEventMarkerAccessor accessor, float frameRate)
        {
            string timeText = "Time: " + accessor.DisplayTimeSeconds.ToString("0.###") + "s";
            if (accessor.TimeIsClipRelative)
            {
                int frameNumber = Mathf.RoundToInt(accessor.DisplayTimeSeconds * frameRate);
                timeText += " · frame " + frameNumber + " at " + frameRate.ToString("0.##") + " fps";
            }

            Label timeLabel = new Label(timeText);
            return timeLabel;
        }

        private VisualElement BuildPayloadRow(IEventMarkerAccessor accessor, AnimEventKeyRegistry registry)
        {
            VisualElement payloadContainer = new VisualElement();
            uint currentKey = accessor.Key;

            if (this.PayloadOverride != null && this.PayloadOverride(currentKey, payloadContainer))
            {
                return payloadContainer;
            }

            AnimEventKeyEntry entry = FindEntry(registry, currentKey);

            VisualElement intField = EventPayloadFieldBuilder.BuildIntField(
                entry,
                accessor.IntParam,
                newValue =>
                {
                    accessor.IntParam = newValue;
                    this.FieldEdited?.Invoke(EventMarkerField.IntParam);
                });
            if (intField != null)
            {
                payloadContainer.Add(intField);
            }

            VisualElement floatField = EventPayloadFieldBuilder.BuildFloatField(
                entry,
                accessor.FloatParam,
                newValue =>
                {
                    accessor.FloatParam = newValue;
                    this.FieldEdited?.Invoke(EventMarkerField.FloatParam);
                });
            if (floatField != null)
            {
                payloadContainer.Add(floatField);
            }

            return payloadContainer;
        }

        private VisualElement BuildWindowRow(IEventMarkerAccessor accessor, float frameRate)
        {
            VisualElement windowRow = new VisualElement();

            IntegerField windowField = new IntegerField("Window (frames)");
            windowField.tooltip = "How many frames the event's AnimEventMask bit stays open. 0 makes it "
                + "pulse-only: it still fires with its payload, it just holds no state.";
            windowField.SetValueWithoutNotify(Mathf.RoundToInt(accessor.WindowSeconds * frameRate));
            windowField.RegisterValueChangedCallback(changeEvent =>
            {
                accessor.WindowSeconds = Mathf.Max(0, changeEvent.newValue) / frameRate;
                this.FieldEdited?.Invoke(EventMarkerField.Window);
            });
            windowRow.Add(windowField);

            if (accessor.WindowSeconds > 0f)
            {
                Label windowHintLabel = new Label(
                    accessor.WindowSeconds.ToString("0.###") + "s at " + frameRate.ToString("0.##") + " fps");
                ApplyHintStyle(windowHintLabel);
                windowRow.Add(windowHintLabel);
            }

            return windowRow;
        }

        private VisualElement BuildFireOnSkipRow(IEventMarkerAccessor accessor)
        {
            Toggle fireOnSkipToggle = new Toggle("Fire On Skip");
            fireOnSkipToggle.tooltip = "Whether a skipped cutscene still fires this event.";
            fireOnSkipToggle.SetValueWithoutNotify(accessor.FireOnSkip);
            fireOnSkipToggle.RegisterValueChangedCallback(changeEvent =>
            {
                accessor.FireOnSkip = changeEvent.newValue;
                this.FieldEdited?.Invoke(EventMarkerField.FireOnSkip);
            });
            return fireOnSkipToggle;
        }

        private VisualElement BuildHoldRow(IEventMarkerAccessor accessor)
        {
            Toggle holdToggle = new Toggle("Hold Until Released");
            holdToggle.tooltip = "Pauses the clock when this event fires until the host releases a "
                + "hold named after the event.";
            holdToggle.SetValueWithoutNotify(accessor.HoldUntilReleased);
            holdToggle.RegisterValueChangedCallback(changeEvent =>
            {
                accessor.HoldUntilReleased = changeEvent.newValue;
                this.FieldEdited?.Invoke(EventMarkerField.HoldUntilReleased);
            });
            return holdToggle;
        }

        private static string ResolveKeyName(AnimEventKeyRegistry registry, uint key)
        {
            string resolvedName = registry?.FindName(key);
            return resolvedName ?? "(unresolved 0x" + key.ToString("X8") + ")";
        }

        private static AnimEventKeyEntry FindEntry(AnimEventKeyRegistry registry, uint key)
        {
            if (registry == null || registry.entries == null)
            {
                return null;
            }

            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = registry.entries[entryIndex];
                if (entry != null && entry.eventKey == key)
                {
                    return entry;
                }
            }

            return null;
        }

        private static float ResolveFrameRate(AnimEventKeyRegistry registry)
        {
            return registry == null || registry.referenceFrameRate < 1f
                ? AnimEventKeyRegistry.DefaultReferenceFrameRate
                : registry.referenceFrameRate;
        }

        private static void ApplyHintStyle(Label hintLabel)
        {
            hintLabel.style.whiteSpace = WhiteSpace.Normal;
            hintLabel.style.opacity = 0.7f;
            hintLabel.style.marginBottom = 4;
        }
    }
}
