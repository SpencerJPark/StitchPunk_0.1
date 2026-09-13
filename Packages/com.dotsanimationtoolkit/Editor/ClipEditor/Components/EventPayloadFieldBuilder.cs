// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Builds the Clip Inspector's intParam/floatParam controls from an event key's payload schema.</summary>
    public static class EventPayloadFieldBuilder
    {
        private const string BaseTooltip =
            "Delivered on the AnimEventOutput pulse. Not carried by the window mask.";

        public static bool HasPayloadSchema(AnimEventKeyEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            bool hasIntValueNames = entry.intParamValueNames != null && entry.intParamValueNames.Count > 0;
            return !string.IsNullOrEmpty(entry.intParamLabel) || hasIntValueNames
                || !string.IsNullOrEmpty(entry.floatParamLabel);
        }

        public static VisualElement BuildIntField(AnimEventKeyEntry entry, int currentValue, Action<int> onChanged)
        {
            if (!HasPayloadSchema(entry))
            {
                return BuildPlainIntField(currentValue, onChanged);
            }

            bool hasIntValueNames = entry.intParamValueNames != null && entry.intParamValueNames.Count > 0;
            if (hasIntValueNames)
            {
                return BuildNamedIntDropdown(entry, currentValue, onChanged);
            }

            if (!string.IsNullOrEmpty(entry.intParamLabel))
            {
                IntegerField labeledIntField = new IntegerField(entry.intParamLabel);
                labeledIntField.tooltip = BaseTooltip;
                labeledIntField.SetValueWithoutNotify(currentValue);
                labeledIntField.RegisterValueChangedCallback(changeEvent => onChanged(changeEvent.newValue));
                return labeledIntField;
            }

            if (currentValue == 0)
            {
                return null;
            }

            IntegerField unusedIntField = new IntegerField("Int Param (unused)");
            unusedIntField.tooltip = BaseTooltip
                + " This event does not use intParam, but this marker still stores a value.";
            unusedIntField.SetValueWithoutNotify(currentValue);
            unusedIntField.RegisterValueChangedCallback(changeEvent => onChanged(changeEvent.newValue));
            SetWarningTint(unusedIntField);
            return unusedIntField;
        }

        public static VisualElement BuildFloatField(
            AnimEventKeyEntry entry, float currentValue, Action<float> onChanged)
        {
            if (!HasPayloadSchema(entry))
            {
                return BuildPlainFloatField(currentValue, onChanged);
            }

            if (!string.IsNullOrEmpty(entry.floatParamLabel))
            {
                FloatField labeledFloatField = new FloatField(entry.floatParamLabel);
                labeledFloatField.tooltip = BaseTooltip;
                labeledFloatField.SetValueWithoutNotify(currentValue);
                labeledFloatField.RegisterValueChangedCallback(changeEvent => onChanged(changeEvent.newValue));

                if (string.IsNullOrEmpty(entry.floatParamUnit))
                {
                    return labeledFloatField;
                }

                return WrapWithUnitLabel(labeledFloatField, entry.floatParamUnit);
            }

            if (currentValue == 0f)
            {
                return null;
            }

            FloatField unusedFloatField = new FloatField("Float Param (unused)");
            unusedFloatField.tooltip = BaseTooltip
                + " This event does not use floatParam, but this marker still stores a value.";
            unusedFloatField.SetValueWithoutNotify(currentValue);
            unusedFloatField.RegisterValueChangedCallback(changeEvent => onChanged(changeEvent.newValue));
            SetWarningTint(unusedFloatField);
            return unusedFloatField;
        }

        private static IntegerField BuildPlainIntField(int currentValue, Action<int> onChanged)
        {
            IntegerField intParamField = new IntegerField("Int Param");
            intParamField.tooltip = BaseTooltip;
            intParamField.SetValueWithoutNotify(currentValue);
            intParamField.RegisterValueChangedCallback(changeEvent => onChanged(changeEvent.newValue));
            return intParamField;
        }

        private static FloatField BuildPlainFloatField(float currentValue, Action<float> onChanged)
        {
            FloatField floatParamField = new FloatField("Float Param");
            floatParamField.tooltip = BaseTooltip;
            floatParamField.SetValueWithoutNotify(currentValue);
            floatParamField.RegisterValueChangedCallback(changeEvent => onChanged(changeEvent.newValue));
            return floatParamField;
        }

        private static DropdownField BuildNamedIntDropdown(
            AnimEventKeyEntry entry, int currentValue, Action<int> onChanged)
        {
            string dropdownLabel = string.IsNullOrEmpty(entry.intParamLabel) ? "Int Param" : entry.intParamLabel;
            List<string> choices = new List<string>(entry.intParamValueNames.Count);
            for (int nameIndex = 0; nameIndex < entry.intParamValueNames.Count; nameIndex++)
            {
                string valueName = entry.intParamValueNames[nameIndex];
                choices.Add(string.IsNullOrEmpty(valueName) ? "(unnamed " + nameIndex + ")" : valueName);
            }

            DropdownField dropdownField = new DropdownField(dropdownLabel, choices, 0);
            dropdownField.tooltip = BaseTooltip;

            bool currentValueIsNamed = currentValue >= 0 && currentValue < choices.Count;
            if (currentValueIsNamed)
            {
                dropdownField.SetValueWithoutNotify(choices[currentValue]);
            }
            else
            {
                dropdownField.SetValueWithoutNotify(currentValue.ToString());
                dropdownField.tooltip = BaseTooltip + " The stored value is outside this event's named range.";
                SetWarningTint(dropdownField);
            }

            dropdownField.RegisterValueChangedCallback(changeEvent =>
            {
                if (dropdownField.index >= 0)
                {
                    dropdownField.tooltip = BaseTooltip;
                    ClearWarningTint(dropdownField);
                    onChanged(dropdownField.index);
                }
            });

            return dropdownField;
        }

        private static VisualElement WrapWithUnitLabel(FloatField floatField, string unitText)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            floatField.style.flexGrow = 1;
            row.Add(floatField);

            Label unitLabel = new Label(unitText);
            unitLabel.style.marginLeft = 4;
            unitLabel.style.opacity = 0.7f;
            row.Add(unitLabel);

            return row;
        }

        private static void SetWarningTint(IntegerField field)
        {
            field.labelElement.style.color = ToolkitPalette.Warning;
        }

        private static void SetWarningTint(FloatField field)
        {
            field.labelElement.style.color = ToolkitPalette.Warning;
        }

        private static void SetWarningTint(DropdownField dropdownField)
        {
            dropdownField.labelElement.style.color = ToolkitPalette.Warning;
            TextElement popupTextElement = dropdownField.Q<TextElement>(
                className: BasePopupField<string, string>.textUssClassName);
            if (popupTextElement != null)
            {
                popupTextElement.style.color = ToolkitPalette.Warning;
            }
        }

        private static void ClearWarningTint(DropdownField dropdownField)
        {
            dropdownField.labelElement.style.color = StyleKeyword.Null;
            TextElement popupTextElement = dropdownField.Q<TextElement>(
                className: BasePopupField<string, string>.textUssClassName);
            if (popupTextElement != null)
            {
                popupTextElement.style.color = StyleKeyword.Null;
            }
        }
    }
}
