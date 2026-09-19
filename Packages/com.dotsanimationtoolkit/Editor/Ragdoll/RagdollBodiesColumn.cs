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
        private readonly List<RagdollBodyDefinition> allBodyDefinitionsInRig = new List<RagdollBodyDefinition>();
        private readonly List<RagdollBodyDefinition> bodyEntries = new List<RagdollBodyDefinition>();
        private readonly List<RigTargetDefinition> availableTargetChoices = new List<RigTargetDefinition>();
        private readonly ListView bodiesListView;
        private readonly ToolbarSearchField bodiesSearchField;
        // Kept alive off-hierarchy: FormatTargetChoice and the choices list back the + button's
        // GenericMenu, even though the popup row itself is gone (one way to add a body, not two).
        private readonly PopupField<RigTargetDefinition> addTargetPopupField;
        private readonly Button addBodyButton;
        private readonly Button deleteBodyButton;

        private RigAsset currentRig;
        private uint selectedBodyId;
        private string bodiesSearchFilterText = string.Empty;

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
            // Bodies/Viewport/Inspector headers all share one baseline.
            headerRow.style.height = 32f;
            headerRow.style.flexShrink = 0f;
            Add(headerRow);

            addBodyButton = ToolkitIcons.MakeIconButton(
                OnAddBodyButtonClicked, "d_Toolbar Plus", "Add a body to the rig", "Add");
            addBodyButton.name = "ragdoll-add-body-button";
            headerActions.Add(addBodyButton);

            deleteBodyButton = ToolkitIcons.MakeIconButton(
                OnDeleteBodyButtonClicked, "TreeEditor.Trash", "Delete the selected body", "Delete");
            deleteBodyButton.name = "ragdoll-delete-body-button";
            ToolkitChrome.StyleButton(deleteBodyButton, ToolkitButtonVariant.Destructive);
            headerActions.Add(deleteBodyButton);

            availableTargetChoices.Add(null);
            addTargetPopupField = new PopupField<RigTargetDefinition>(
                availableTargetChoices, 0, FormatTargetChoice, FormatTargetChoice);
            addTargetPopupField.name = "ragdoll-add-body-target-field";

            bodiesSearchField = new ToolbarSearchField();
            bodiesSearchField.name = "ragdoll-bodies-search";
            bodiesSearchField.tooltip = "Search bodies";
            bodiesSearchField.style.flexShrink = 0f;
            bodiesSearchField.RegisterValueChangedCallback(OnBodiesSearchFieldValueChanged);
            Add(bodiesSearchField);

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
            // The selected tone is a class on the row, so rows re-bind to move it.
            bodiesListView.RefreshItems();
        }

        public void Refresh()
        {
            allBodyDefinitionsInRig.Clear();
            if (currentRig != null && currentRig.ragdollBodies != null)
            {
                foreach (RagdollBodyDefinition candidateBody in currentRig.ragdollBodies)
                {
                    if (candidateBody != null)
                    {
                        allBodyDefinitionsInRig.Add(candidateBody);
                    }
                }
            }

            ApplyBodiesSearchFilter();
            RefreshAddTargetPopupChoices();
            UpdateButtonStates();
        }

        // Filtering and rig-refresh share this one path so a search never drifts from what
        // Refresh() would otherwise show.
        private void ApplyBodiesSearchFilter()
        {
            bodyEntries.Clear();
            string trimmedFilterText = string.IsNullOrEmpty(bodiesSearchFilterText)
                ? string.Empty
                : bodiesSearchFilterText.Trim();

            foreach (RagdollBodyDefinition candidateBody in allBodyDefinitionsInRig)
            {
                if (trimmedFilterText.Length == 0 || BodyMatchesSearchFilter(candidateBody, trimmedFilterText))
                {
                    bodyEntries.Add(candidateBody);
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
        }

        private void OnBodiesSearchFieldValueChanged(ChangeEvent<string> changeEvent)
        {
            bodiesSearchFilterText = changeEvent.newValue ?? string.Empty;
            ApplyBodiesSearchFilter();
        }

        private bool BodyMatchesSearchFilter(RagdollBodyDefinition bodyDefinition, string filterText)
        {
            if (bodyDefinition == null)
            {
                return false;
            }

            string rowTitle = ResolveBodyRowTitle(bodyDefinition);
            return rowTitle.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // The + button's only job now: open a menu of every addable target, anchored to the
        // button, so there is exactly one way to add a body (the popup row is gone).
        private void OnAddBodyButtonClicked()
        {
            if (currentRig == null)
            {
                return;
            }

            GenericMenu targetChoiceMenu = new GenericMenu();
            bool hasAnyAddableTarget = false;
            foreach (RigTargetDefinition candidateTargetDefinition in availableTargetChoices)
            {
                if (candidateTargetDefinition == null || candidateTargetDefinition.Id.Value == 0u)
                {
                    continue;
                }

                hasAnyAddableTarget = true;
                RigTargetDefinition capturedTargetDefinition = candidateTargetDefinition;
                targetChoiceMenu.AddItem(
                    new GUIContent(FormatTargetChoice(capturedTargetDefinition)),
                    false,
                    () => AddBodyForTarget(capturedTargetDefinition));
            }

            if (!hasAnyAddableTarget)
            {
                targetChoiceMenu.AddDisabledItem(new GUIContent(FormatTargetChoice(null)));
            }

            targetChoiceMenu.DropDown(addBodyButton.worldBound);
        }

        private void AddBodyForTarget(RigTargetDefinition selectedTargetDefinition)
        {
            if (currentRig == null)
            {
                return;
            }

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
            // The selected tone is a class on the row, so rows re-bind to move it.
            bodiesListView.RefreshItems();
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

            Label rowMetaLabel = new Label();
            rowMetaLabel.name = "ragdoll-body-row-meta";
            rowMetaLabel.AddToClassList("toolkit-list-row__meta");
            row.Add(rowMetaLabel);

            return itemSlot;
        }

        private void BindBodyRow(VisualElement element, int index)
        {
            VisualElement row = element?.Q<VisualElement>("ragdoll-body-row");
            Label rowLabel = element?.Q<Label>(className: "toolkit-list-row__title");
            Label rowMetaLabel = element?.Q<Label>("ragdoll-body-row-meta");
            if (row == null || rowLabel == null || rowMetaLabel == null || index < 0 || index >= bodyEntries.Count)
            {
                return;
            }

            RagdollBodyDefinition bodyDefinition = bodyEntries[index];
            string rowText = ResolveBodyRowTitle(bodyDefinition);

            bool isBodyResolved = RagdollBodySummaryResolver.IsBodyResolved(currentRig, bodyDefinition);
            rowLabel.text = isBodyResolved ? rowText : rowText + "  (unresolved)";
            rowLabel.EnableInClassList("toolkit-text--warning", !isBodyResolved);

            // Every body is a box collider (see RagdollBodyDefinition); "root" means no other
            // ragdoll body sits above it in the addressed hierarchy.
            bool isRootBody = isBodyResolved && !RagdollBodySummaryResolver.HasParentBody(currentRig, bodyDefinition);
            rowMetaLabel.text = isRootBody ? "box · root" : "box";

            row.EnableInClassList(
                "toolkit-list-row--selected",
                bodyDefinition != null && bodyDefinition.Id.Value == selectedBodyId);
        }

        private string ResolveBodyRowTitle(RagdollBodyDefinition bodyDefinition)
        {
            if (bodyDefinition == null)
            {
                return "(unnamed body)";
            }

            string resolvedNodeName = RagdollBodySummaryResolver.ResolveNodePath(currentRig, bodyDefinition);
            return !string.IsNullOrEmpty(bodyDefinition.displayName)
                ? bodyDefinition.displayName
                : (!string.IsNullOrEmpty(resolvedNodeName) ? resolvedNodeName : "(unnamed body)");
        }
    }
}
