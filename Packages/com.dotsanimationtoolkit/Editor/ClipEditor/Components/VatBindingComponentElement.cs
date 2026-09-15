// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One part's VAT binding row: the source clip field, the loop-safe toggle and a length
    /// mismatch hint. Works detached from the component stack — construct it, drive its fields.
    /// </summary>
    public sealed class VatBindingComponentElement : VisualElement
    {
        private const string BoxRowUssClassName = "toolkit-box__row";
        private const string BoxLabelUssClassName = "toolkit-box__label";
        private const string HintUssClassName = "toolkit-hint";

        private readonly ClipAsset clip;
        private readonly VatBakeSource source;
        private readonly System.Action onEdited;

        private readonly ObjectField sourceField;
        private readonly Label hintLabel;
        private readonly Toggle loopSafeToggle;
        private readonly Label lengthLabel;

        internal ObjectField SourceField
        {
            get { return sourceField; }
        }

        internal Toggle LoopSafeToggle
        {
            get { return loopSafeToggle; }
        }

        internal Label LengthLabel
        {
            get { return lengthLabel; }
        }

        public VatBindingComponentElement(ClipAsset clip, VatBakeSource source, System.Action onEdited)
        {
            this.clip = clip;
            this.source = source;
            this.onEdited = onEdited;

            VisualElement nameRow = new VisualElement();
            nameRow.AddToClassList(BoxRowUssClassName);
            Label nameLabel = new Label(source.DisplayName);
            nameLabel.AddToClassList(BoxLabelUssClassName);
            nameRow.Add(nameLabel);
            Add(nameRow);

            VisualElement sourceRow = new VisualElement();
            sourceRow.AddToClassList(BoxRowUssClassName);
            sourceField = new ObjectField("Source")
            {
                objectType = typeof(AnimationClip),
                allowSceneObjects = false,
                tooltip = "The imported clip the VAT bake samples for this part. Empty: the bake poses the authored bone tracks."
            };
            sourceField.RegisterValueChangedCallback(OnSourceFieldChanged);
            sourceRow.Add(sourceField);
            Add(sourceRow);

            VisualElement hintRow = new VisualElement();
            hintRow.AddToClassList(BoxRowUssClassName);
            hintLabel = new Label();
            hintLabel.AddToClassList(HintUssClassName);
            hintRow.Add(hintLabel);
            Add(hintRow);

            VisualElement loopSafeRow = new VisualElement();
            loopSafeRow.AddToClassList(BoxRowUssClassName);
            loopSafeToggle = new Toggle("Loop safe")
            {
                tooltip = "Appends a copy of frame 0 so a looping clip's last frame blends back to the first without a seam."
            };
            loopSafeToggle.RegisterValueChangedCallback(OnLoopSafeToggleChanged);
            loopSafeRow.Add(loopSafeToggle);
            Add(loopSafeRow);

            VisualElement lengthRow = new VisualElement();
            lengthRow.AddToClassList(BoxRowUssClassName);
            lengthLabel = new Label();
            lengthLabel.AddToClassList(HintUssClassName);
            lengthRow.Add(lengthLabel);
            Add(lengthRow);

            Refresh();
        }

        public void Refresh()
        {
            AnimationClip sourceClip = ClipVatBindingEditing.GetSourceClip(clip, source.TargetId);
            sourceField.SetValueWithoutNotify(sourceClip);
            hintLabel.text = sourceClip == null ? "Empty — the bake uses the authored bone tracks." : string.Empty;
            loopSafeToggle.SetValueWithoutNotify(ClipVatBindingEditing.GetLoopSafe(clip, source.TargetId));
            loopSafeToggle.SetEnabled(sourceClip != null);
            lengthLabel.text = sourceClip == null
                ? string.Empty
                : ClipVatBindingEditing.DescribeLengthMismatch(clip, sourceClip);
        }

        private void OnSourceFieldChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            ClipVatBindingEditing.SetSourceClip(clip, source.TargetId, changeEvent.newValue as AnimationClip);
            Refresh();
            onEdited?.Invoke();
        }

        private void OnLoopSafeToggleChanged(ChangeEvent<bool> changeEvent)
        {
            ClipVatBindingEditing.SetLoopSafe(clip, source.TargetId, changeEvent.newValue);
            Refresh();
            onEdited?.Invoke();
        }
    }
}
