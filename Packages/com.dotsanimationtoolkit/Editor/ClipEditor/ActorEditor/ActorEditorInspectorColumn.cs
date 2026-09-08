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
    /// <summary>
    /// The Actor Editor's right-hand inspector: profile, layer or animation fields for whatever
    /// <see cref="SetSelection"/> names, polled each tick by <see cref="RefreshIfChanged"/> rather
    /// than wired to the tree that owns the selection.
    /// </summary>
    public sealed class ActorEditorInspectorColumn : VisualElement
    {
        private static readonly Direction[] AllSlotsInOrder = DirectionSetClipQueueView.SlotOrder;

        private ActorProfileAsset profile;
        private ActorPreviewComposer composer;
        private ActorEditorSelection selection;
        private bool hasBuiltContentOnce;

        // Slots the author revealed with "+ Add Clip" beyond the current target's required ones.
        private readonly HashSet<Direction> extraVisibleDirectionSlots = new HashSet<Direction>();
        private readonly List<Direction> visibleDirectionSlots = new List<Direction>();

        // Snapshot of the animation's direction slots as last rendered, polled in RefreshIfChanged
        // to catch an inspector-external edit (undo, another tool) the way DirectionSetsPanel does.
        private readonly ClipAsset[] observedDirectionSlots = new ClipAsset[AllSlotsInOrder.Length];
        private AnimationDirections observedTargetDirections;

        // Profile block.
        private ObjectField rigField;
        private VisualElement clipSetListContainer;
        private EnumField turnDirectionsField;

        // Layer block.
        private TextField layerNameField;
        private Toggle layerDefaultActiveToggle;
        private Button layerStarterButton;

        // Animation block.
        private Button animationNameButton;
        private Toggle hasDirectionsToggle;
        private ObjectField clipField;
        private VisualElement directionContainer;
        private DirectionSetClipQueueView queueView;
        private EnumField targetDirectionsField;
        private Label coverageLabel;
        private Button addClipButton;
        private EnumField loopField;
        private FloatField speedField;
        private Toggle useClipDefaultBlendToggle;
        private FloatField blendInField;
        private EnumField ragdollTriggerField;
        private Button ragdollAtEventButton;
        private Label layerReadoutLabel;
        private Label playingStatusLabel;

        /// <summary>Raised after any write this column makes to the bound profile.</summary>
        public event Action ProfileEdited;

        public ActorEditorInspectorColumn()
        {
            style.flexGrow = 1f;
        }

        /// <summary>Points this column at a profile and the composer whose playback state its "Playing"/"Stopped" readout reads. <paramref name="composerInstance"/> may be null.</summary>
        public void Bind(ActorProfileAsset profileAsset, ActorPreviewComposer composerInstance)
        {
            profile = profileAsset;
            composer = composerInstance;
            RebuildContent();
        }

        /// <summary>Rebuilds the shown block only when the selected kind or index actually changed.</summary>
        public void SetSelection(ActorEditorSelection newSelection)
        {
            bool selectionUnchanged = hasBuiltContentOnce
                && selection.kind == newSelection.kind
                && selection.layerIndex == newSelection.layerIndex
                && selection.animationIndex == newSelection.animationIndex;
            selection = newSelection;
            if (!selectionUnchanged)
            {
                RebuildContent();
            }
        }

        /// <summary>Re-reads the selected object's fields into the built controls; never rebuilds the pane itself.</summary>
        public void RefreshIfChanged()
        {
            if (profile == null)
            {
                return;
            }

            switch (selection.kind)
            {
                case ActorEditorSelectionKind.Layer:
                    RefreshLayerBlockIfChanged();
                    break;
                case ActorEditorSelectionKind.Animation:
                    RefreshAnimationBlockIfChanged();
                    break;
                default:
                    RefreshProfileBlockIfChanged();
                    break;
            }
        }

        // -----------------------------------------------------------------------------------
        // Selection resolution.
        // -----------------------------------------------------------------------------------

        private ActorLayerDefinition ResolveSelectedLayer()
        {
            if (profile == null || profile.layers == null
                || selection.layerIndex < 0 || selection.layerIndex >= profile.layers.Count)
            {
                return null;
            }
            return profile.layers[selection.layerIndex];
        }

        private ActorAnimationDefinition ResolveSelectedAnimation()
        {
            ActorLayerDefinition layer = ResolveSelectedLayer();
            if (layer == null || layer.animations == null
                || selection.animationIndex < 0 || selection.animationIndex >= layer.animations.Count)
            {
                return null;
            }
            return layer.animations[selection.animationIndex];
        }

        // -----------------------------------------------------------------------------------
        // Rebuild — the only place that tears controls down and puts new ones up.
        // -----------------------------------------------------------------------------------

        private void RebuildContent()
        {
            Clear();
            ClearFieldReferences();
            extraVisibleDirectionSlots.Clear();
            hasBuiltContentOnce = true;

            if (profile == null)
            {
                Add(new HelpBox("No profile assigned.", HelpBoxMessageType.Info));
                return;
            }

            if (selection.kind == ActorEditorSelectionKind.Layer)
            {
                ActorLayerDefinition layer = ResolveSelectedLayer();
                if (layer != null)
                {
                    Add(BuildLayerBlock(layer));
                    return;
                }
            }
            else if (selection.kind == ActorEditorSelectionKind.Animation)
            {
                ActorLayerDefinition layer = ResolveSelectedLayer();
                ActorAnimationDefinition animation = ResolveSelectedAnimation();
                if (layer != null && animation != null)
                {
                    Add(BuildAnimationBlock(layer, animation));
                    return;
                }
            }

            Add(BuildProfileBlock());
        }

        private void ClearFieldReferences()
        {
            rigField = null;
            clipSetListContainer = null;
            turnDirectionsField = null;
            layerNameField = null;
            layerDefaultActiveToggle = null;
            layerStarterButton = null;
            animationNameButton = null;
            hasDirectionsToggle = null;
            clipField = null;
            directionContainer = null;
            queueView = null;
            targetDirectionsField = null;
            coverageLabel = null;
            addClipButton = null;
            loopField = null;
            speedField = null;
            useClipDefaultBlendToggle = null;
            blendInField = null;
            ragdollTriggerField = null;
            ragdollAtEventButton = null;
            layerReadoutLabel = null;
            playingStatusLabel = null;
        }

        // -----------------------------------------------------------------------------------
        // Profile block.
        // -----------------------------------------------------------------------------------

        private VisualElement BuildProfileBlock()
        {
            VisualElement container = new VisualElement();

            rigField = new ObjectField("Rig")
            {
                objectType = typeof(RigAsset),
                name = "actor-editor-inspector-rig-field"
            };
            rigField.SetValueWithoutNotify(profile.rig);
            rigField.RegisterValueChangedCallback(
                changeEvent => ApplyRigChange(changeEvent.newValue as RigAsset));
            container.Add(rigField);

            Label clipSetsHeading = new Label("Clip Sets");
            clipSetsHeading.style.unityFontStyleAndWeight = FontStyle.Bold;
            clipSetsHeading.style.marginTop = 6f;
            container.Add(clipSetsHeading);

            clipSetListContainer = new VisualElement { name = "actor-editor-inspector-clipset-list" };
            container.Add(clipSetListContainer);
            RebuildClipSetRows();

            Button addClipSetButton = new Button(AddClipSetRow)
            {
                text = "+ Clip Set",
                name = "actor-editor-inspector-add-clipset-button"
            };
            container.Add(addClipSetButton);

            turnDirectionsField = new EnumField("Turn Directions", profile.turnDirections)
            {
                name = "actor-editor-inspector-turn-directions-field"
            };
            turnDirectionsField.style.marginTop = 6f;
            turnDirectionsField.RegisterValueChangedCallback(
                changeEvent => ApplyTurnDirectionsChange((AnimationDirections)changeEvent.newValue));
            container.Add(turnDirectionsField);

            return container;
        }

        private void ApplyRigChange(RigAsset rig)
        {
            Undo.RecordObject(profile, "Set Actor Profile Rig");
            profile.rig = rig;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void ApplyTurnDirectionsChange(AnimationDirections turnDirections)
        {
            Undo.RecordObject(profile, "Set Actor Profile Turn Directions");
            profile.turnDirections = turnDirections;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void AddClipSetRow()
        {
            Undo.RecordObject(profile, "Add Clip Set");
            if (profile.clipSets == null)
            {
                profile.clipSets = new List<ClipSetAsset>();
            }
            profile.clipSets.Add(null);
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            RebuildClipSetRows();
        }

        private void RemoveClipSetRow(int clipSetIndex)
        {
            if (profile.clipSets == null || clipSetIndex < 0 || clipSetIndex >= profile.clipSets.Count)
            {
                return;
            }
            Undo.RecordObject(profile, "Remove Clip Set");
            profile.clipSets.RemoveAt(clipSetIndex);
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            RebuildClipSetRows();
        }

        private void SetClipSetAt(int clipSetIndex, ClipSetAsset clipSet)
        {
            if (profile.clipSets == null || clipSetIndex < 0 || clipSetIndex >= profile.clipSets.Count)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Clip Set");
            profile.clipSets[clipSetIndex] = clipSet;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void RebuildClipSetRows()
        {
            if (clipSetListContainer == null)
            {
                return;
            }
            clipSetListContainer.Clear();
            if (profile.clipSets == null)
            {
                return;
            }

            for (int clipSetIndex = 0; clipSetIndex < profile.clipSets.Count; clipSetIndex++)
            {
                int capturedIndex = clipSetIndex;

                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;

                ObjectField clipSetField = new ObjectField { objectType = typeof(ClipSetAsset) };
                clipSetField.style.flexGrow = 1f;
                clipSetField.SetValueWithoutNotify(profile.clipSets[capturedIndex]);
                clipSetField.RegisterValueChangedCallback(
                    changeEvent => SetClipSetAt(capturedIndex, changeEvent.newValue as ClipSetAsset));
                row.Add(clipSetField);

                Button removeButton = new Button(() => RemoveClipSetRow(capturedIndex)) { text = "×" };
                row.Add(removeButton);

                clipSetListContainer.Add(row);
            }
        }

        private void RefreshProfileBlockIfChanged()
        {
            if (rigField != null && !IsBeingEdited(rigField) && !Equals(rigField.value, profile.rig))
            {
                rigField.SetValueWithoutNotify(profile.rig);
            }
            if (turnDirectionsField != null && !IsBeingEdited(turnDirectionsField)
                && !Equals(turnDirectionsField.value, profile.turnDirections))
            {
                turnDirectionsField.SetValueWithoutNotify(profile.turnDirections);
            }

            int clipSetCount = profile.clipSets != null ? profile.clipSets.Count : 0;
            if (clipSetListContainer != null && clipSetListContainer.childCount != clipSetCount)
            {
                RebuildClipSetRows();
            }
        }

        // -----------------------------------------------------------------------------------
        // Layer block.
        // -----------------------------------------------------------------------------------

        private VisualElement BuildLayerBlock(ActorLayerDefinition layer)
        {
            VisualElement container = new VisualElement();

            bool isBaseBookend = selection.layerIndex == 0;
            bool isOverrideBookend = profile.layers != null && selection.layerIndex == profile.layers.Count - 1;
            bool isBookend = isBaseBookend || isOverrideBookend;

            layerNameField = new TextField("Name") { name = "actor-editor-inspector-layer-name-field" };
            layerNameField.SetValueWithoutNotify(layer.displayName);
            layerNameField.isReadOnly = isBookend;
            if (!isBookend)
            {
                layerNameField.RegisterValueChangedCallback(
                    changeEvent => ApplyLayerNameChange(changeEvent.newValue));
            }
            container.Add(layerNameField);

            if (isBookend)
            {
                HelpBox bookendHelpBox = new HelpBox(
                    isBaseBookend
                        ? ActorProfileAsset.BaseLayerName + " is the fixed first layer; its name cannot change."
                        : ActorProfileAsset.OverrideLayerName + " is the fixed last layer; its name cannot change.",
                    HelpBoxMessageType.Info)
                {
                    name = "actor-editor-inspector-layer-bookend-helpbox"
                };
                container.Add(bookendHelpBox);
            }

            layerDefaultActiveToggle = new Toggle("Default Active")
            {
                name = "actor-editor-inspector-layer-default-active-toggle"
            };
            layerDefaultActiveToggle.SetValueWithoutNotify(layer.defaultActive);
            layerDefaultActiveToggle.RegisterValueChangedCallback(
                changeEvent => ApplyLayerDefaultActiveChange(changeEvent.newValue));
            container.Add(layerDefaultActiveToggle);

            layerStarterButton = new Button
            {
                name = "actor-editor-inspector-layer-starter-button",
                text = DescribeStarter(layer)
            };
            layerStarterButton.clicked += () =>
            {
                ActorLayerDefinition selectedLayer = ResolveSelectedLayer();
                if (selectedLayer != null)
                {
                    OpenStarterDropdown(layerStarterButton, selectedLayer);
                }
            };
            container.Add(layerStarterButton);

            return container;
        }

        private void ApplyLayerNameChange(string displayName)
        {
            ActorLayerDefinition layer = ResolveSelectedLayer();
            if (layer == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Rename Layer");
            layer.displayName = displayName;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void ApplyLayerDefaultActiveChange(bool defaultActive)
        {
            ActorLayerDefinition layer = ResolveSelectedLayer();
            if (layer == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Toggle Layer Default Active");
            layer.defaultActive = defaultActive;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void OpenStarterDropdown(Button anchor, ActorLayerDefinition layer)
        {
            GenericDropdownMenu menu = new GenericDropdownMenu();
            menu.AddItem("(none)", layer.startingAnimationKey == 0u, () => SetStarter(0u));

            AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
            if (layer.animations != null)
            {
                for (int animationIndex = 0; animationIndex < layer.animations.Count; animationIndex++)
                {
                    ActorAnimationDefinition animation = layer.animations[animationIndex];
                    if (animation == null)
                    {
                        continue;
                    }
                    uint animationKey = animation.animationKey;
                    string resolvedName = registry != null ? registry.FindName(animationKey) : null;
                    string label = resolvedName ?? "(unresolved 0x" + animationKey.ToString("X8") + ")";
                    menu.AddItem(label, layer.startingAnimationKey == animationKey, () => SetStarter(animationKey));
                }
            }

            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        private void SetStarter(uint animationKey)
        {
            ActorLayerDefinition layer = ResolveSelectedLayer();
            if (layer == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Layer Starter");
            layer.startingAnimationKey = animationKey;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            if (layerStarterButton != null)
            {
                layerStarterButton.text = DescribeStarter(layer);
            }
        }

        private static string DescribeStarter(ActorLayerDefinition layer)
        {
            return layer == null || layer.startingAnimationKey == 0u
                ? "(none)"
                : DescribeAnimationName(layer.startingAnimationKey);
        }

        private void RefreshLayerBlockIfChanged()
        {
            ActorLayerDefinition layer = ResolveSelectedLayer();
            if (layer == null)
            {
                return;
            }

            if (layerNameField != null && !IsBeingEdited(layerNameField) && layerNameField.value != layer.displayName)
            {
                layerNameField.SetValueWithoutNotify(layer.displayName);
            }
            if (layerDefaultActiveToggle != null && !IsBeingEdited(layerDefaultActiveToggle)
                && layerDefaultActiveToggle.value != layer.defaultActive)
            {
                layerDefaultActiveToggle.SetValueWithoutNotify(layer.defaultActive);
            }
            if (layerStarterButton != null)
            {
                string expectedStarterText = DescribeStarter(layer);
                if (layerStarterButton.text != expectedStarterText)
                {
                    layerStarterButton.text = expectedStarterText;
                }
            }
        }

        // -----------------------------------------------------------------------------------
        // Animation block.
        // -----------------------------------------------------------------------------------

        private VisualElement BuildAnimationBlock(ActorLayerDefinition layer, ActorAnimationDefinition animation)
        {
            if (animation.directionSlots == null)
            {
                animation.directionSlots = new DirectionSlots();
            }

            VisualElement container = new VisualElement();

            animationNameButton = new Button
            {
                name = "actor-editor-inspector-animation-name-button",
                text = DescribeAnimationName(animation.animationKey)
            };
            animationNameButton.clicked += () => OpenAnimationNamePicker(animationNameButton);
            container.Add(animationNameButton);

            hasDirectionsToggle = new Toggle("Direction dimension")
            {
                name = "actor-editor-inspector-animation-has-directions-toggle"
            };
            hasDirectionsToggle.SetValueWithoutNotify(animation.hasDirections);
            hasDirectionsToggle.RegisterValueChangedCallback(
                changeEvent => ApplyHasDirectionsChange(changeEvent.newValue));
            container.Add(hasDirectionsToggle);

            clipField = new ObjectField("Clip")
            {
                objectType = typeof(ClipAsset),
                name = "actor-editor-inspector-animation-clip-field"
            };
            clipField.SetValueWithoutNotify(animation.clip);
            clipField.RegisterValueChangedCallback(
                changeEvent => ApplyClipChange(changeEvent.newValue as ClipAsset));
            container.Add(clipField);

            directionContainer = new VisualElement { name = "actor-editor-inspector-direction-container" };

            targetDirectionsField = new EnumField("Target Directions", animation.directionSlots.targetDirections)
            {
                name = "actor-editor-inspector-target-directions-field"
            };
            targetDirectionsField.RegisterValueChangedCallback(
                changeEvent => ApplyTargetDirectionsChange((AnimationDirections)changeEvent.newValue));
            directionContainer.Add(targetDirectionsField);

            queueView = new DirectionSetClipQueueView { name = "actor-editor-inspector-direction-queue-view" };
            queueView.SlotAssigned += OnDirectionSlotAssigned;
            queueView.SlotMoved += OnDirectionSlotMoved;
            queueView.SlotCleared += OnDirectionSlotCleared;
            queueView.OpenClipRequested += OnDirectionClipOpenRequested;
            directionContainer.Add(queueView);

            coverageLabel = new Label { name = "actor-editor-inspector-coverage-label" };
            coverageLabel.style.whiteSpace = WhiteSpace.Normal;
            directionContainer.Add(coverageLabel);

            addClipButton = new Button(AddNextUnfilledDirectionSlot)
            {
                text = "+ Add Clip",
                name = "actor-editor-inspector-add-clip-button"
            };
            directionContainer.Add(addClipButton);

            container.Add(directionContainer);

            ApplyDirectionDimensionVisibility(animation);
            RebuildDirectionQueue(animation);

            loopField = new EnumField("Loop", animation.loop) { name = "actor-editor-inspector-loop-field" };
            loopField.RegisterValueChangedCallback(changeEvent => ApplyLoopChange((LoopMode)changeEvent.newValue));
            container.Add(loopField);

            speedField = new FloatField("Speed") { name = "actor-editor-inspector-speed-field" };
            speedField.SetValueWithoutNotify(animation.speed);
            speedField.RegisterValueChangedCallback(changeEvent => ApplySpeedChange(changeEvent.newValue));
            container.Add(speedField);

            bool usesClipDefaultBlend = float.IsNaN(animation.blendIn);

            useClipDefaultBlendToggle = new Toggle("Use Clip Default Blend-In")
            {
                name = "actor-editor-inspector-blend-in-use-default-toggle"
            };
            useClipDefaultBlendToggle.SetValueWithoutNotify(usesClipDefaultBlend);
            useClipDefaultBlendToggle.RegisterValueChangedCallback(
                changeEvent => ApplyBlendInUseClipDefaultChange(changeEvent.newValue));
            container.Add(useClipDefaultBlendToggle);

            blendInField = new FloatField("Blend In") { name = "actor-editor-inspector-blend-in-field" };
            blendInField.SetValueWithoutNotify(usesClipDefaultBlend ? 0f : animation.blendIn);
            blendInField.style.display = usesClipDefaultBlend ? DisplayStyle.None : DisplayStyle.Flex;
            blendInField.RegisterValueChangedCallback(changeEvent => ApplyBlendInChange(changeEvent.newValue));
            container.Add(blendInField);

            ragdollTriggerField = new EnumField("Ragdoll Trigger", animation.ragdollTrigger)
            {
                name = "actor-editor-inspector-ragdoll-trigger-field"
            };
            ragdollTriggerField.RegisterValueChangedCallback(
                changeEvent => ApplyRagdollTriggerChange((RagdollTrigger)changeEvent.newValue));
            container.Add(ragdollTriggerField);

            ragdollAtEventButton = new Button
            {
                name = "actor-editor-inspector-ragdoll-at-event-button",
                text = DescribeRagdollAtEvent(animation.ragdollAtEventKey)
            };
            ragdollAtEventButton.clicked += () => OpenRagdollAtEventPicker(ragdollAtEventButton);
            container.Add(ragdollAtEventButton);
            ApplyRagdollAtEventVisibility(animation);

            layerReadoutLabel = new Label("Layer: " + DescribeLayerName(layer))
            {
                name = "actor-editor-inspector-layer-readout-label"
            };
            container.Add(layerReadoutLabel);

            playingStatusLabel = new Label { name = "actor-editor-inspector-playing-status-label" };
            container.Add(playingStatusLabel);
            RefreshPlayingStatusLabel(animation);

            return container;
        }

        private void ApplyClipChange(ClipAsset clip)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Animation Clip");
            animation.clip = clip;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void ApplyHasDirectionsChange(bool hasDirections)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Toggle Animation Direction Dimension");
            animation.hasDirections = hasDirections;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            ApplyDirectionDimensionVisibility(animation);
            if (hasDirections)
            {
                RebuildDirectionQueue(animation);
            }
        }

        private void ApplyDirectionDimensionVisibility(ActorAnimationDefinition animation)
        {
            bool hasDirections = animation != null && animation.hasDirections;
            if (clipField != null)
            {
                clipField.style.display = hasDirections ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (directionContainer != null)
            {
                directionContainer.style.display = hasDirections ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ApplyTargetDirectionsChange(AnimationDirections targetDirections)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null || animation.directionSlots == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Target Directions");
            animation.directionSlots.targetDirections = targetDirections;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            RebuildDirectionQueue(animation);
        }

        private void OnDirectionSlotAssigned(Direction slot, ClipAsset clip)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null || animation.directionSlots == null || animation.directionSlots.GetSlot(slot) == clip)
            {
                return;
            }
            Undo.RecordObject(profile, "Assign Direction Slot");
            animation.directionSlots.SetSlot(slot, clip);
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            RebuildDirectionQueue(animation);
        }

        private void OnDirectionSlotMoved(Direction fromSlot, Direction toSlot)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null || animation.directionSlots == null)
            {
                return;
            }
            ClipAsset movedClip = animation.directionSlots.GetSlot(fromSlot);
            Undo.RecordObject(profile, "Move Direction Slot");
            animation.directionSlots.SetSlot(fromSlot, null);
            animation.directionSlots.SetSlot(toSlot, movedClip);
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            extraVisibleDirectionSlots.Add(fromSlot);
            extraVisibleDirectionSlots.Add(toSlot);
            RebuildDirectionQueue(animation);
        }

        private void OnDirectionSlotCleared(Direction slot)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null || animation.directionSlots == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Clear Direction Slot");
            animation.directionSlots.SetSlot(slot, null);
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            extraVisibleDirectionSlots.Remove(slot);
            RebuildDirectionQueue(animation);
        }

        private static void OnDirectionClipOpenRequested(ClipAsset clip)
        {
            if (clip == null)
            {
                return;
            }
            ClipEditorWindow.FocusClipEditing();
            EditorGUIUtility.PingObject(clip);
            Selection.activeObject = clip;
        }

        private void AddNextUnfilledDirectionSlot()
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null || animation.directionSlots == null)
            {
                return;
            }
            for (int slotIndex = 0; slotIndex < AllSlotsInOrder.Length; slotIndex++)
            {
                Direction slot = AllSlotsInOrder[slotIndex];
                if (!visibleDirectionSlots.Contains(slot))
                {
                    extraVisibleDirectionSlots.Add(slot);
                    RebuildDirectionQueue(animation);
                    return;
                }
            }
        }

        private void RebuildDirectionQueue(ActorAnimationDefinition animation)
        {
            DirectionSlots slots = animation != null ? animation.directionSlots : null;
            RecomputeVisibleDirectionSlots(slots);

            if (queueView != null)
            {
                queueView.Rebuild(slots, visibleDirectionSlots, null);
            }
            if (addClipButton != null)
            {
                addClipButton.SetEnabled(slots != null && visibleDirectionSlots.Count < AllSlotsInOrder.Length);
            }
            if (targetDirectionsField != null && slots != null)
            {
                targetDirectionsField.SetValueWithoutNotify(slots.targetDirections);
            }

            RefreshCoverageLabel(slots);
            CaptureObservedDirectionSlots(slots);
        }

        private void RecomputeVisibleDirectionSlots(DirectionSlots slots)
        {
            visibleDirectionSlots.Clear();
            if (slots == null)
            {
                return;
            }

            Direction[] requiredSlots = DirectionSlots.GetRequiredSlots(slots.targetDirections);
            for (int slotIndex = 0; slotIndex < AllSlotsInOrder.Length; slotIndex++)
            {
                Direction slot = AllSlotsInOrder[slotIndex];
                bool isRequired = Array.IndexOf(requiredSlots, slot) >= 0;
                if (isRequired || slots.GetSlot(slot) != null || extraVisibleDirectionSlots.Contains(slot))
                {
                    visibleDirectionSlots.Add(slot);
                }
            }
        }

        // Matches the wording DirectionSetsPanel showed for the same coverage question, expressed
        // against the target coverage rather than just the fill, since a profile's authoring intent
        // ("of Six") is what tells the author how far along the set is.
        private void RefreshCoverageLabel(DirectionSlots slots)
        {
            if (coverageLabel == null)
            {
                return;
            }
            if (slots == null)
            {
                coverageLabel.text = string.Empty;
                return;
            }

            bool isValidFill = slots.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);
            List<string> missingNames = new List<string>();
            Direction[] requiredSlots = DirectionSlots.GetRequiredSlots(slots.targetDirections);
            for (int slotIndex = 0; slotIndex < requiredSlots.Length; slotIndex++)
            {
                if (slots.GetSlot(requiredSlots[slotIndex]) == null)
                {
                    missingNames.Add(DirectionSetClipQueueView.ShortName(requiredSlots[slotIndex]));
                }
            }

            string coverageText = "Covers " + effectiveDirections + " of " + slots.targetDirections;
            if (!isValidFill)
            {
                coverageText += " — invalid fill pattern, rounded down";
            }
            else if (missingNames.Count > 0)
            {
                coverageText += " — missing " + string.Join(", ", missingNames);
            }
            coverageLabel.text = coverageText;
        }

        private void CaptureObservedDirectionSlots(DirectionSlots slots)
        {
            for (int slotIndex = 0; slotIndex < AllSlotsInOrder.Length; slotIndex++)
            {
                observedDirectionSlots[slotIndex] = slots != null ? slots.GetSlot(AllSlotsInOrder[slotIndex]) : null;
            }
            observedTargetDirections = slots != null ? slots.targetDirections : observedTargetDirections;
        }

        private bool HasDirectionSlotsChangedSinceObserved(DirectionSlots slots)
        {
            if (slots == null)
            {
                return false;
            }
            if (slots.targetDirections != observedTargetDirections)
            {
                return true;
            }
            for (int slotIndex = 0; slotIndex < AllSlotsInOrder.Length; slotIndex++)
            {
                if (slots.GetSlot(AllSlotsInOrder[slotIndex]) != observedDirectionSlots[slotIndex])
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyLoopChange(LoopMode loop)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Animation Loop");
            animation.loop = loop;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void ApplySpeedChange(float speed)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Animation Speed");
            animation.speed = speed;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void ApplyBlendInUseClipDefaultChange(bool useClipDefault)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Toggle Blend-In Use Clip Default");
            animation.blendIn = useClipDefault ? float.NaN : 0f;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            if (blendInField != null)
            {
                blendInField.SetValueWithoutNotify(animation.blendIn);
                blendInField.style.display = useClipDefault ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void ApplyBlendInChange(float blendIn)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Animation Blend-In");
            animation.blendIn = blendIn;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
        }

        private void ApplyRagdollTriggerChange(RagdollTrigger ragdollTrigger)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Set Ragdoll Trigger");
            animation.ragdollTrigger = ragdollTrigger;
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            ApplyRagdollAtEventVisibility(animation);
        }

        private void ApplyRagdollAtEventVisibility(ActorAnimationDefinition animation)
        {
            if (ragdollAtEventButton == null)
            {
                return;
            }
            bool showAtEventPicker = animation != null && animation.ragdollTrigger != RagdollTrigger.None;
            ragdollAtEventButton.style.display = showAtEventPicker ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OpenAnimationNamePicker(Button anchor)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
            VocabularyPicker.Open(
                this,
                anchor,
                registry,
                registry,
                VocabularyPickerConfig.ForAnimationNames(registry),
                chosenKey =>
                {
                    Undo.RecordObject(profile, "Set Animation Name");
                    animation.animationKey = chosenKey;
                    EditorUtility.SetDirty(profile);
                    ProfileEdited?.Invoke();
                    anchor.text = DescribeAnimationName(chosenKey);
                },
                () => { });
        }

        private void OpenRagdollAtEventPicker(Button anchor)
        {
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }
            AnimEventKeyRegistry registry = VocabularyRegistryProvider.AnimEventKeys;
            VocabularyPicker.Open(
                this,
                anchor,
                registry,
                registry,
                BuildRagdollAtEventPickerConfig(registry),
                chosenKey =>
                {
                    Undo.RecordObject(profile, "Set Ragdoll At-Event");
                    animation.ragdollAtEventKey = chosenKey;
                    EditorUtility.SetDirty(profile);
                    ProfileEdited?.Invoke();
                    anchor.text = DescribeRagdollAtEvent(chosenKey);
                },
                () => { });
        }

        // Same rows as ForEventKeys, plus a "(at play)" row for the 0 sentinel — the one case an
        // event picker elsewhere never needs, since a ragdoll trigger not tied to a marker fires
        // the instant its animation starts.
        private static VocabularyPickerConfig BuildRagdollAtEventPickerConfig(AnimEventKeyRegistry registry)
        {
            VocabularyPickerConfig eventConfig = VocabularyPickerConfig.ForEventKeys(registry);
            return new VocabularyPickerConfig(
                "(at play)",
                "Trigger ragdoll the instant this animation starts, rather than waiting for a marker.",
                eventConfig.CreateRowNoun,
                eventConfig.EditButtonLabel,
                eventConfig.EditRowLabel,
                eventConfig.EditRowDescription,
                eventConfig.QuickEditWindowTitle,
                eventConfig.QuickEditMissingMessage,
                eventConfig.DescribeEntryId);
        }

        private static string DescribeAnimationName(uint animationKey)
        {
            if (animationKey == 0u)
            {
                return "(none)";
            }
            AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
            string resolvedName = registry != null ? registry.FindName(animationKey) : null;
            return resolvedName ?? "(unresolved 0x" + animationKey.ToString("X8") + ")";
        }

        private static string DescribeRagdollAtEvent(uint eventKey)
        {
            if (eventKey == 0u)
            {
                return "(at play)";
            }
            AnimEventKeyRegistry registry = VocabularyRegistryProvider.AnimEventKeys;
            string resolvedName = registry != null ? registry.FindName(eventKey) : null;
            return resolvedName ?? "(unresolved 0x" + eventKey.ToString("X8") + ")";
        }

        private static string DescribeLayerName(ActorLayerDefinition layer)
        {
            return layer != null && !string.IsNullOrEmpty(layer.displayName) ? layer.displayName : "(unnamed)";
        }

        private void RefreshPlayingStatusLabel(ActorAnimationDefinition animation)
        {
            if (playingStatusLabel == null)
            {
                return;
            }
            if (composer == null || !composer.IsCreated || animation == null)
            {
                playingStatusLabel.text = "Playing: —";
                return;
            }

            bool isThisEntryActive = composer.LayerAnimationKey(selection.layerIndex) == animation.animationKey
                && (composer.LayerFlags(selection.layerIndex) & PlaybackFlags.Active) != 0;
            playingStatusLabel.text = isThisEntryActive ? "Playing" : "Stopped";
        }

        private void RefreshAnimationBlockIfChanged()
        {
            ActorLayerDefinition layer = ResolveSelectedLayer();
            ActorAnimationDefinition animation = ResolveSelectedAnimation();
            if (animation == null)
            {
                return;
            }

            if (animationNameButton != null)
            {
                string expectedName = DescribeAnimationName(animation.animationKey);
                if (animationNameButton.text != expectedName)
                {
                    animationNameButton.text = expectedName;
                }
            }

            if (hasDirectionsToggle != null && !IsBeingEdited(hasDirectionsToggle)
                && hasDirectionsToggle.value != animation.hasDirections)
            {
                hasDirectionsToggle.SetValueWithoutNotify(animation.hasDirections);
                ApplyDirectionDimensionVisibility(animation);
                if (animation.hasDirections)
                {
                    RebuildDirectionQueue(animation);
                }
            }

            if (clipField != null && !IsBeingEdited(clipField) && !Equals(clipField.value, animation.clip))
            {
                clipField.SetValueWithoutNotify(animation.clip);
            }

            if (animation.hasDirections && animation.directionSlots != null
                && HasDirectionSlotsChangedSinceObserved(animation.directionSlots))
            {
                RebuildDirectionQueue(animation);
            }

            if (loopField != null && !IsBeingEdited(loopField) && !Equals(loopField.value, animation.loop))
            {
                loopField.SetValueWithoutNotify(animation.loop);
            }
            if (speedField != null && !IsBeingEdited(speedField) && !Mathf.Approximately(speedField.value, animation.speed))
            {
                speedField.SetValueWithoutNotify(animation.speed);
            }

            bool usesClipDefaultBlend = float.IsNaN(animation.blendIn);
            if (useClipDefaultBlendToggle != null && !IsBeingEdited(useClipDefaultBlendToggle)
                && useClipDefaultBlendToggle.value != usesClipDefaultBlend)
            {
                useClipDefaultBlendToggle.SetValueWithoutNotify(usesClipDefaultBlend);
                if (blendInField != null)
                {
                    blendInField.style.display = usesClipDefaultBlend ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }
            if (blendInField != null && !usesClipDefaultBlend && !IsBeingEdited(blendInField)
                && !Mathf.Approximately(blendInField.value, animation.blendIn))
            {
                blendInField.SetValueWithoutNotify(animation.blendIn);
            }

            if (ragdollTriggerField != null && !IsBeingEdited(ragdollTriggerField)
                && !Equals(ragdollTriggerField.value, animation.ragdollTrigger))
            {
                ragdollTriggerField.SetValueWithoutNotify(animation.ragdollTrigger);
                ApplyRagdollAtEventVisibility(animation);
            }
            if (ragdollAtEventButton != null)
            {
                string expectedAtEventText = DescribeRagdollAtEvent(animation.ragdollAtEventKey);
                if (ragdollAtEventButton.text != expectedAtEventText)
                {
                    ragdollAtEventButton.text = expectedAtEventText;
                }
            }

            if (layerReadoutLabel != null && layer != null)
            {
                string expectedLayerText = "Layer: " + DescribeLayerName(layer);
                if (layerReadoutLabel.text != expectedLayerText)
                {
                    layerReadoutLabel.text = expectedLayerText;
                }
            }

            RefreshPlayingStatusLabel(animation);
        }

        // -----------------------------------------------------------------------------------
        // Shared field-editing guard.
        // -----------------------------------------------------------------------------------

        // The capture test is not redundant with the focus test: a field's drag handle captures the
        // mouse without focusing the input behind it, so focus alone misses a number being dragged.
        private static bool IsBeingEdited(VisualElement field)
        {
            if (field == null || field.panel == null)
            {
                return false;
            }

            VisualElement capturingElement = field.panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement;
            if (capturingElement != null && (capturingElement == field || field.Contains(capturingElement)))
            {
                return true;
            }

            VisualElement focusedElement = field.panel.focusController.focusedElement as VisualElement;
            return focusedElement != null && (focusedElement == field || field.Contains(focusedElement));
        }
    }
}
