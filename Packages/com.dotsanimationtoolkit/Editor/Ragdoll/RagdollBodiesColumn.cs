// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Ragdoll tab's left column: every body on the rig, with add and delete.</summary>
    public sealed class RagdollBodiesColumn : VisualElement
    {
        private readonly List<RagdollBodyDefinition> bodyEntries = new List<RagdollBodyDefinition>();
        private readonly List<RigTargetDefinition> availableTargetChoices = new List<RigTargetDefinition>();
        private readonly ListView bodiesListView;
        private readonly PopupField<RigTargetDefinition> addTargetPopupField;
        private readonly Button addBodyButton;
        private readonly Button deleteBodyButton;

        private RigAsset currentRig;
        private uint selectedBodyId;

        public event Action<uint> BodySelected;
        public event Action RigBodiesChanged;

        public uint SelectedBodyId
        {
            get { return selectedBodyId; }
        }

        public RagdollBodiesColumn()
        {
            name = "ragdoll-bodies-column";
            AddToClassList("toolkit-column");

            VisualElement headerRow = ToolkitChrome.MakePaneHeader(
                "Bodies", out Label titleLabel, out VisualElement headerActions);
            Add(headerRow);

            addBodyButton = ToolkitIcons.MakeIconTextButton(
                OnAddBodyButtonClicked, "d_Toolbar Plus", "Add a body to the rig", "Add");
            addBodyButton.name = "ragdoll-add-body-button";
            headerActions.Add(addBodyButton);

            deleteBodyButton = ToolkitIcons.MakeIconTextButton(
                OnDeleteBodyButtonClicked, "TreeEditor.Trash", "Delete the selected body", "Delete");
            deleteBodyButton.name = "ragdoll-delete-body-button";
            headerActions.Add(deleteBodyButton);

            availableTargetChoices.Add(null);
            addTargetPopupField = new PopupField<RigTargetDefinition>(
                availableTargetChoices, 0, FormatTargetChoice, FormatTargetChoice);
            addTargetPopupField.name = "ragdoll-add-body-target-field";
            // A dropdown in a column stretches to fill it when it grows; this one is a control,
            // not content, so it keeps its own height and the bodies list takes the space.
            addTargetPopupField.style.flexGrow = 0f;
            addTargetPopupField.style.flexShrink = 0f;
            Add(addTargetPopupField);

            bodiesListView = new ListView();
            bodiesListView.name = "ragdoll-bodies-list";
            bodiesListView.selectionType = SelectionType.Single;
            bodiesListView.fixedItemHeight = 22f;
            bodiesListView.style.flexGrow = 1f;
            bodiesListView.makeItem = MakeBodyRow;
            bodiesListView.bindItem = BindBodyRow;
            bodiesListView.itemsSource = bodyEntries;
            bodiesListView.selectionChanged += OnBodiesListSelectionChanged;
            bodiesListView.AddToClassList("toolkit-list-surface");
            Add(bodiesListView);

            UpdateButtonStates();
        }

        public void SetRig(RigAsset rig)
        {
            currentRig = rig;
            selectedBodyId = 0u;
            Refresh();
        }

        public void SetSelectedBodyId(uint bodyId)
        {
            selectedBodyId = bodyId;
            int matchingIndex = FindIndexOfBodyId(bodyId);
            if (matchingIndex >= 0)
            {
                bodiesListView.SetSelectionWithoutNotify(new int[] { matchingIndex });
            }
            else
            {
                bodiesListView.SetSelectionWithoutNotify(new int[0]);
            }
            UpdateButtonStates();
        }

        public void Refresh()
        {
            bodyEntries.Clear();
            if (currentRig != null && currentRig.ragdollBodies != null)
            {
                foreach (RagdollBodyDefinition candidateBody in currentRig.ragdollBodies)
                {
                    if (candidateBody != null)
                    {
                        bodyEntries.Add(candidateBody);
                    }
                }
            }

            bodiesListView.Rebuild();

            int matchingIndex = FindIndexOfBodyId(selectedBodyId);
            if (matchingIndex >= 0)
            {
                bodiesListView.SetSelectionWithoutNotify(new int[] { matchingIndex });
            }
            else
            {
                selectedBodyId = 0u;
                bodiesListView.SetSelectionWithoutNotify(new int[0]);
            }

            RefreshAddTargetPopupChoices();
            UpdateButtonStates();
        }

        private void OnAddBodyButtonClicked()
        {
            if (currentRig == null)
            {
                return;
            }

            RigTargetDefinition selectedTargetDefinition = addTargetPopupField.value;
            if (selectedTargetDefinition == null || selectedTargetDefinition.Id.Value == 0u)
            {
                return;
            }

            RigNodeAddress newBodyAddress = new RigNodeAddress
            {
                kind = RigNodeAddressKind.RigTarget,
                targetId = selectedTargetDefinition.Id.Value,
            };
            string newBodyDisplayName = string.IsNullOrEmpty(selectedTargetDefinition.displayName)
                ? "(unnamed part)"
                : selectedTargetDefinition.displayName;

            RagdollBodyDefinition addedBodyDefinition = RagdollBodyEditing.AddBody(
                currentRig, newBodyAddress, newBodyDisplayName);
            if (addedBodyDefinition == null)
            {
                return;
            }

            selectedBodyId = addedBodyDefinition.Id.Value;
            Refresh();
            BodySelected?.Invoke(selectedBodyId);
            RigBodiesChanged?.Invoke();
        }

        private void OnDeleteBodyButtonClicked()
        {
            if (currentRig == null || selectedBodyId == 0u)
            {
                return;
            }

            RagdollBodyDefinition bodyToDelete = RagdollBodyEditing.FindBodyById(currentRig, selectedBodyId);
            string bodyDisplayName = bodyToDelete != null && !string.IsNullOrEmpty(bodyToDelete.displayName)
                ? bodyToDelete.displayName
                : "this body";

            bool deleteWasConfirmed = EditorUtility.DisplayDialog(
                "Delete ragdoll body", $"Delete '{bodyDisplayName}'?", "Delete", "Cancel");
            if (!deleteWasConfirmed)
            {
                return;
            }

            bool bodyWasRemoved = RagdollBodyEditing.RemoveBody(currentRig, selectedBodyId);
            if (!bodyWasRemoved)
            {
                return;
            }

            selectedBodyId = 0u;
            bodiesListView.SetSelectionWithoutNotify(new int[0]);
            Refresh();
            RigBodiesChanged?.Invoke();
        }

        private void OnBodiesListSelectionChanged(IEnumerable<object> selectedItems)
        {
            RagdollBodyDefinition selectedBodyDefinition = null;
            foreach (object selectedItem in selectedItems)
            {
                selectedBodyDefinition = selectedItem as RagdollBodyDefinition;
                break;
            }

            selectedBodyId = selectedBodyDefinition != null ? selectedBodyDefinition.Id.Value : 0u;
            UpdateButtonStates();
            BodySelected?.Invoke(selectedBodyId);
        }

        private void RefreshAddTargetPopupChoices()
        {
            availableTargetChoices.Clear();
            if (currentRig != null && currentRig.targets != null)
            {
                foreach (RigTargetDefinition candidateTarget in currentRig.targets)
                {
                    if (candidateTarget != null && candidateTarget.Id.Value != 0u)
                    {
                        availableTargetChoices.Add(candidateTarget);
                    }
                }
            }

            if (availableTargetChoices.Count == 0)
            {
                availableTargetChoices.Add(null);
            }

            addTargetPopupField.choices = availableTargetChoices;
            addTargetPopupField.SetValueWithoutNotify(availableTargetChoices[0]);
        }

        private void UpdateButtonStates()
        {
            bool hasRig = currentRig != null;
            addBodyButton.SetEnabled(hasRig);
            deleteBodyButton.SetEnabled(hasRig && selectedBodyId != 0u);
        }

        private int FindIndexOfBodyId(uint bodyId)
        {
            if (bodyId == 0u)
            {
                return -1;
            }

            for (int bodyIndex = 0; bodyIndex < bodyEntries.Count; bodyIndex++)
            {
                RagdollBodyDefinition candidateBody = bodyEntries[bodyIndex];
                if (candidateBody != null && candidateBody.Id.Value == bodyId)
                {
                    return bodyIndex;
                }
            }

            return -1;
        }

        private static string FormatTargetChoice(RigTargetDefinition target)
        {
            if (target == null)
            {
                return "(no targets)";
            }

            return string.IsNullOrEmpty(target.displayName) ? "(unnamed part)" : target.displayName;
        }

        private static VisualElement MakeBodyRow()
        {
            VisualElement itemSlot = ToolkitChrome.MakeListRowSlot("ragdoll-body-row", out VisualElement row);
            Label rowLabel = new Label();
            rowLabel.AddToClassList("toolkit-list-row__title");
            row.Add(rowLabel);
            return itemSlot;
        }

        private void BindBodyRow(VisualElement element, int index)
        {
            Label rowLabel = element?.Q<Label>(className: "toolkit-list-row__title");
            if (rowLabel == null || index < 0 || index >= bodyEntries.Count)
            {
                return;
            }

            RagdollBodyDefinition bodyDefinition = bodyEntries[index];
            string resolvedNodeName = RagdollBodySummaryResolver.ResolveNodePath(currentRig, bodyDefinition);
            string rowText = !string.IsNullOrEmpty(bodyDefinition.displayName)
                ? bodyDefinition.displayName
                : (!string.IsNullOrEmpty(resolvedNodeName) ? resolvedNodeName : "(unnamed body)");

            bool isBodyResolved = RagdollBodySummaryResolver.IsBodyResolved(currentRig, bodyDefinition);
            rowLabel.text = isBodyResolved ? rowText : rowText + "  (unresolved)";
            rowLabel.EnableInClassList("toolkit-text--warning", !isBodyResolved);
        }
    }
}
