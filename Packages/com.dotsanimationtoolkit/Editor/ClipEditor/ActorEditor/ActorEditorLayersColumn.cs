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
    /// The Actor Editor's left column: every layer and its animations, live-indicated against a
    /// running <see cref="ActorPreviewComposer"/>. Every write goes through <see cref="Undo"/> and
    /// raises <see cref="ProfileEdited"/>; the tree itself is rebuilt only when <see cref="RefreshIfChanged"/>
    /// finds the profile's shape actually differs, never from inside a field's own callback.
    /// </summary>
    public sealed class ActorEditorLayersColumn : VisualElement
    {
        private const string RootUssClassName = "actor-editor__layers-column-root";
        private const string LayerBoxUssClassName = "toolkit-box";
        private const string LayerBoxSelectedUssClassName = "toolkit-box--selected";
        private const string BoxHeaderUssClassName = "toolkit-box__header";
        private const string BoxTitleUssClassName = "toolkit-box__title";
        private const string BoxBodyUssClassName = "toolkit-box__body";
        private const string BoxRowUssClassName = "toolkit-box__row";
        private const string BoxRowSelectedUssClassName = "toolkit-box__row--selected";
        private const string BoxFooterUssClassName = "toolkit-box__footer";
        private const string EyeToggleUssClassName = "toolkit-eye-toggle";
        private const string EyeToggleOffUssClassName = "toolkit-eye-toggle--off";
        private const string TransportFieldUssClassName = "toolkit-transport__field";
        private const string LiveDotUssClassName = "actor-editor__live-dot";
        private const string LiveDotActiveUssClassName = "actor-editor__live-dot--active";
        private const string LayerRowNamePrefix = "actor-editor-layer-row-";
        private const string LayerEyeNamePrefix = "actor-editor-layer-eye-";
        private const string AnimationRowNamePrefix = "actor-editor-animation-row-";

        private readonly ScrollView rowScroll;

        private ActorProfileAsset profile;
        private ActorPreviewComposer composer;

        private bool hasBuiltOnce;
        private int lastFingerprint;

        private ActorEditorSelection currentSelection = ActorEditorSelection.None;
        private VisualElement selectedRowElement;

        private readonly Dictionary<int, Label> layerLiveDotsByLayerIndex = new Dictionary<int, Label>();

        private readonly Dictionary<(int layerIndex, int animationIndex), Label> animationLiveDots =
            new Dictionary<(int layerIndex, int animationIndex), Label>();

        private readonly Dictionary<(int layerIndex, int animationIndex), FloatField> animationScrubFields =
            new Dictionary<(int layerIndex, int animationIndex), FloatField>();

        /// <summary>Raised when a layer or animation row is clicked.</summary>
        public event Action<ActorEditorSelection> SelectionChanged;

        /// <summary>Raised after any write this column makes to the bound profile.</summary>
        public event Action ProfileEdited;

        public ActorEditorLayersColumn()
        {
            style.flexGrow = 1f;
            AddToClassList(RootUssClassName);

            rowScroll = new ScrollView(ScrollViewMode.Vertical);
            rowScroll.style.flexGrow = 1f;
            Add(rowScroll);
        }

        /// <summary>Points the column at a profile and the composer previewing it, and rebuilds immediately.</summary>
        public void Bind(ActorProfileAsset boundProfile, ActorPreviewComposer previewComposer)
        {
            profile = boundProfile;
            composer = previewComposer;
            currentSelection = ActorEditorSelection.None;
            selectedRowElement = null;
            hasBuiltOnce = false;
            RefreshIfChanged();
        }

        /// <summary>
        /// Rebuilds the tree only when the profile's shape (layer count, per-layer animation count,
        /// or which names are on it) has actually changed since the last call — an inspector edit or
        /// an undo lands here without either of them telling this column directly.
        /// </summary>
        public void RefreshIfChanged()
        {
            int fingerprint = ComputeFingerprint();
            if (hasBuiltOnce && fingerprint == lastFingerprint)
            {
                RefreshLiveIndicators();
                return;
            }

            lastFingerprint = fingerprint;
            hasBuiltOnce = true;
            Rebuild();
            RefreshLiveIndicators();
        }

        private int ComputeFingerprint()
        {
            unchecked
            {
                int fingerprint = 17;
                int layerCount = profile != null && profile.layers != null ? profile.layers.Count : 0;
                fingerprint = (fingerprint * 31) + layerCount;

                for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    ActorLayerDefinition layer = profile.layers[layerIndex];
                    fingerprint = (fingerprint * 31)
                        + (layer != null && layer.displayName != null ? layer.displayName.GetHashCode() : 0);
                    fingerprint = (fingerprint * 31) + (layer != null && layer.defaultActive ? 1 : 0);
                    fingerprint = (fingerprint * 31) + (layer != null ? (int)layer.startingAnimationKey : 0);

                    int animationCount = layer != null && layer.animations != null ? layer.animations.Count : 0;
                    fingerprint = (fingerprint * 31) + animationCount;
                    for (int animationIndex = 0; animationIndex < animationCount; animationIndex++)
                    {
                        ActorAnimationDefinition animation = layer.animations[animationIndex];
                        fingerprint = (fingerprint * 31) + (animation != null ? (int)animation.animationKey : 0);
                    }
                }

                return fingerprint;
            }
        }

        private void Rebuild()
        {
            rowScroll.Clear();
            layerLiveDotsByLayerIndex.Clear();
            animationLiveDots.Clear();
            animationScrubFields.Clear();
            selectedRowElement = null;

            if (profile == null || profile.layers == null)
            {
                Label emptyLabel = new Label("No profile assigned.");
                emptyLabel.style.whiteSpace = WhiteSpace.Normal;
                rowScroll.Add(emptyLabel);
                return;
            }

            for (int layerIndex = 0; layerIndex < profile.layers.Count; layerIndex++)
            {
                rowScroll.Add(BuildLayerBlock(layerIndex));
            }

        }

        private VisualElement BuildLayerBlock(int layerIndex)
        {
            ActorLayerDefinition layer = profile.layers[layerIndex];
            bool isBookend = layerIndex == 0 || layerIndex == profile.layers.Count - 1;

            VisualElement block = new VisualElement();
            block.AddToClassList(LayerBoxUssClassName);

            VisualElement headerRow = new VisualElement { name = LayerRowNamePrefix + layerIndex };
            headerRow.AddToClassList(BoxHeaderUssClassName);
            headerRow.RegisterCallback<PointerDownEvent>(pointerEvent => SelectLayer(layerIndex, block));
            block.Add(headerRow);

            bool defaultActive = layer != null && layer.defaultActive;
            Button eyeButton = ToolkitIcons.MakeIconButton(
                null,
                defaultActive ? ToolkitIcons.EyeOpen : ToolkitIcons.EyeClosed,
                "Start with this layer active. The preview re-seeds when you change it.",
                defaultActive ? "on" : "off");
            eyeButton.AddToClassList(EyeToggleUssClassName);
            eyeButton.EnableInClassList(EyeToggleOffUssClassName, !defaultActive);
            eyeButton.name = LayerEyeNamePrefix + layerIndex;
            eyeButton.clicked += () => SetLayerDefaultActive(layerIndex, !profile.layers[layerIndex].defaultActive);
            headerRow.Add(eyeButton);

            Label liveDot = new Label("●");
            liveDot.AddToClassList(LiveDotUssClassName);
            headerRow.Add(liveDot);
            layerLiveDotsByLayerIndex[layerIndex] = liveDot;

            Label nameLabel = new Label(layer != null && !string.IsNullOrEmpty(layer.displayName)
                ? layer.displayName : "Layer " + layerIndex);
            nameLabel.AddToClassList(BoxTitleUssClassName);
            headerRow.Add(nameLabel);

            Button starterButton = new Button { text = ResolveAnimationDisplayName(layer != null ? layer.startingAnimationKey : 0u) };
            starterButton.tooltip = "The animation this layer starts on at bake.";
            starterButton.style.minWidth = 70f;
            starterButton.clicked += () => OpenStarterMenu(layerIndex, starterButton);
            headerRow.Add(starterButton);

            if (!isBookend)
            {
                Button moveUpButton = ToolkitIcons.MakeIconButton(
                    () => MoveLayer(layerIndex, layerIndex - 1),
                    null,
                    "Move this layer toward Base. Reordering layers changes priority.",
                    "▲");
                headerRow.Add(moveUpButton);

                Button moveDownButton = ToolkitIcons.MakeIconButton(
                    () => MoveLayer(layerIndex, layerIndex + 1),
                    null,
                    "Move this layer toward Override. Reordering layers changes priority.",
                    "▼");
                headerRow.Add(moveDownButton);

                Button deleteButton = ToolkitIcons.MakeIconButton(
                    () => DeleteLayer(layerIndex),
                    ToolkitIcons.Trash,
                    "Delete this layer and every animation on it.",
                    "×");
                headerRow.Add(deleteButton);
            }

            VisualElement animationsBody = new VisualElement();
            animationsBody.AddToClassList(BoxBodyUssClassName);
            block.Add(animationsBody);

            int animationCount = layer != null && layer.animations != null ? layer.animations.Count : 0;
            for (int animationIndex = 0; animationIndex < animationCount; animationIndex++)
            {
                animationsBody.Add(BuildAnimationRow(layerIndex, animationIndex));
            }

            VisualElement footer = new VisualElement();
            footer.AddToClassList(BoxFooterUssClassName);
            Button addAnimationButton = ToolkitIcons.MakeIconButton(null, ToolkitIcons.Plus, "Add an animation to this layer.", "+");
            addAnimationButton.clicked += () => OpenAddAnimationPicker(layerIndex, addAnimationButton);
            footer.Add(addAnimationButton);
            block.Add(footer);

            return block;
        }

        private VisualElement BuildAnimationRow(int layerIndex, int animationIndex)
        {
            ActorLayerDefinition layer = profile.layers[layerIndex];
            ActorAnimationDefinition animation = layer.animations[animationIndex];
            uint animationKey = animation != null ? animation.animationKey : 0u;

            VisualElement row = new VisualElement { name = AnimationRowNamePrefix + layerIndex + "-" + animationIndex };
            row.AddToClassList(BoxRowUssClassName);
            row.RegisterCallback<PointerDownEvent>(pointerEvent => SelectAnimation(layerIndex, animationIndex, row));

            Label liveDot = new Label("●");
            liveDot.AddToClassList(LiveDotUssClassName);
            row.Add(liveDot);
            animationLiveDots[(layerIndex, animationIndex)] = liveDot;

            Label nameLabel = new Label(ResolveAnimationDisplayName(animationKey));
            nameLabel.style.flexGrow = 1f;
            row.Add(nameLabel);

            Button playButton = ToolkitIcons.MakeIconButton(
                () => composer?.PlayAnimation(animationKey), ToolkitIcons.Play, "Play this animation on the preview.", "▶");
            row.Add(playButton);

            Button stopButton = ToolkitIcons.MakeIconButton(
                () => composer?.StopAnimation(animationKey), ToolkitIcons.Stop, "Stop this animation on the preview.", "■");
            row.Add(stopButton);

            FloatField scrubField = new FloatField
            {
                value = composer != null ? composer.LayerTime(layerIndex) : 0f
            };
            scrubField.AddToClassList(TransportFieldUssClassName);
            scrubField.tooltip = "This layer's playhead, seconds.";
            scrubField.RegisterValueChangedCallback(changeEvent => composer?.SetLayerTime(layerIndex, changeEvent.newValue));
            row.Add(scrubField);
            animationScrubFields[(layerIndex, animationIndex)] = scrubField;

            return row;
        }

        private void RefreshLiveIndicators()
        {
            if (composer == null || profile == null || profile.layers == null)
            {
                return;
            }

            foreach (KeyValuePair<int, Label> entry in layerLiveDotsByLayerIndex)
            {
                bool isActive = (composer.LayerFlags(entry.Key) & PlaybackFlags.Active) != 0;
                entry.Value.EnableInClassList(LiveDotActiveUssClassName, isActive);
            }

            foreach (KeyValuePair<(int layerIndex, int animationIndex), Label> entry in animationLiveDots)
            {
                ActorLayerDefinition layer = profile.layers[entry.Key.layerIndex];
                ActorAnimationDefinition animation = layer != null && layer.animations != null
                    && entry.Key.animationIndex < layer.animations.Count
                    ? layer.animations[entry.Key.animationIndex] : null;
                uint animationKey = animation != null ? animation.animationKey : 0u;
                bool isActive = animationKey != 0u
                    && composer.LayerAnimationKey(entry.Key.layerIndex) == animationKey
                    && (composer.LayerFlags(entry.Key.layerIndex) & PlaybackFlags.Active) != 0;
                entry.Value.EnableInClassList(LiveDotActiveUssClassName, isActive);
            }

            foreach (KeyValuePair<(int layerIndex, int animationIndex), FloatField> entry in animationScrubFields)
            {
                bool isBeingEdited = entry.Value.panel != null
                    && entry.Value.panel.focusController != null
                    && entry.Value.panel.focusController.focusedElement == entry.Value;
                if (!isBeingEdited)
                {
                    entry.Value.SetValueWithoutNotify(composer.LayerTime(entry.Key.layerIndex));
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // Selection
        // -----------------------------------------------------------------------------------------

        private void SelectLayer(int layerIndex, VisualElement rowElement)
        {
            ApplySelection(
                new ActorEditorSelection { kind = ActorEditorSelectionKind.Layer, layerIndex = layerIndex },
                rowElement, LayerBoxSelectedUssClassName);
        }

        private void SelectAnimation(int layerIndex, int animationIndex, VisualElement rowElement)
        {
            ApplySelection(
                new ActorEditorSelection
                {
                    kind = ActorEditorSelectionKind.Animation, layerIndex = layerIndex, animationIndex = animationIndex
                },
                rowElement, BoxRowSelectedUssClassName);
        }

        private void ApplySelection(ActorEditorSelection selection, VisualElement rowElement, string selectedUssClassName)
        {
            if (selectedRowElement != null)
            {
                selectedRowElement.RemoveFromClassList(LayerBoxSelectedUssClassName);
                selectedRowElement.RemoveFromClassList(BoxRowSelectedUssClassName);
            }
            currentSelection = selection;
            selectedRowElement = rowElement;
            selectedRowElement?.AddToClassList(selectedUssClassName);
            SelectionChanged?.Invoke(currentSelection);
        }

        // -----------------------------------------------------------------------------------------
        // Writes — every one records undo, marks the asset dirty, raises ProfileEdited, then
        // re-polls immediately so this column's own edits look identical to an external one landing.
        // -----------------------------------------------------------------------------------------

        private void ApplyProfileEdit(string undoLabel, Action mutate)
        {
            if (profile == null)
            {
                return;
            }
            Undo.RecordObject(profile, undoLabel);
            mutate();
            EditorUtility.SetDirty(profile);
            ProfileEdited?.Invoke();
            RefreshIfChanged();
        }

        private void SetLayerDefaultActive(int layerIndex, bool defaultActive)
        {
            if (profile == null || profile.layers == null || layerIndex < 0 || layerIndex >= profile.layers.Count)
            {
                return;
            }
            ApplyProfileEdit("Set Layer Default Active", () => profile.layers[layerIndex].defaultActive = defaultActive);
        }

        // Test seam: EditMode fixtures build this column outside a panel, so a dispatched ClickEvent
        // never reaches the eye button's click handler. Exercises the exact same write path.
        public void ToggleLayerDefaultActive(int layerIndex)
        {
            if (profile == null || profile.layers == null || layerIndex < 0 || layerIndex >= profile.layers.Count)
            {
                return;
            }
            SetLayerDefaultActive(layerIndex, !profile.layers[layerIndex].defaultActive);
        }

        /// <summary>Inserts a fresh layer directly above the fixed Override bookend. No-op past <see cref="ActorProfileAsset.MaxLayerCount"/>.</summary>
        public void AddLayer()
        {
            if (profile == null || profile.layers == null || profile.layers.Count >= ActorProfileAsset.MaxLayerCount)
            {
                return;
            }
            ApplyProfileEdit("Add Actor Layer", () =>
            {
                int insertIndex = profile.layers.Count - 1; // above the fixed Override bookend
                profile.layers.Insert(insertIndex, new ActorLayerDefinition { displayName = "Layer " + insertIndex });
            });
        }

        private void DeleteLayer(int layerIndex)
        {
            if (profile == null || profile.layers == null)
            {
                return;
            }
            if (layerIndex <= 0 || layerIndex >= profile.layers.Count - 1)
            {
                return; // the Base and Override bookends are never deletable
            }
            ApplyProfileEdit("Delete Actor Layer", () => profile.layers.RemoveAt(layerIndex));
        }

        /// <summary>Moves a non-bookend layer to another non-bookend slot. Any move touching a bookend is silently reverted.</summary>
        public void MoveLayer(int fromIndex, int toIndex)
        {
            if (profile == null || profile.layers == null)
            {
                return;
            }
            int lastIndex = profile.layers.Count - 1;
            bool fromIsBookend = fromIndex <= 0 || fromIndex >= lastIndex;
            bool toIsBookend = toIndex <= 0 || toIndex >= lastIndex;
            if (fromIsBookend || toIsBookend || fromIndex == toIndex)
            {
                return;
            }
            ApplyProfileEdit("Reorder Actor Layer", () =>
            {
                ActorLayerDefinition movedLayer = profile.layers[fromIndex];
                profile.layers.RemoveAt(fromIndex);
                profile.layers.Insert(toIndex, movedLayer);
            });
        }

        private void SetLayerStarter(int layerIndex, uint animationKey)
        {
            if (profile == null || profile.layers == null || layerIndex < 0 || layerIndex >= profile.layers.Count)
            {
                return;
            }
            ApplyProfileEdit("Set Layer Starter", () => profile.layers[layerIndex].startingAnimationKey = animationKey);
        }

        private void AddAnimationToLayer(int layerIndex, uint animationKey)
        {
            if (animationKey == 0u || profile == null || profile.layers == null
                || layerIndex < 0 || layerIndex >= profile.layers.Count)
            {
                return;
            }
            ApplyProfileEdit("Add Actor Animation", () =>
            {
                ActorLayerDefinition layer = profile.layers[layerIndex];
                if (layer.animations == null)
                {
                    layer.animations = new List<ActorAnimationDefinition>();
                }
                layer.animations.Add(new ActorAnimationDefinition { animationKey = animationKey });
            });
        }

        // -----------------------------------------------------------------------------------------
        // Pickers
        // -----------------------------------------------------------------------------------------

        private void OpenStarterMenu(int layerIndex, VisualElement anchor)
        {
            ActorLayerDefinition layer = profile.layers[layerIndex];
            GenericDropdownMenu starterMenu = new GenericDropdownMenu();
            starterMenu.AddItem("(none)", layer.startingAnimationKey == 0u, () => SetLayerStarter(layerIndex, 0u));

            if (layer.animations != null)
            {
                for (int animationIndex = 0; animationIndex < layer.animations.Count; animationIndex++)
                {
                    ActorAnimationDefinition animation = layer.animations[animationIndex];
                    if (animation == null || animation.animationKey == 0u)
                    {
                        continue;
                    }
                    uint capturedKey = animation.animationKey;
                    starterMenu.AddItem(
                        ResolveAnimationDisplayName(capturedKey),
                        layer.startingAnimationKey == capturedKey,
                        () => SetLayerStarter(layerIndex, capturedKey));
                }
            }

            starterMenu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        private void OpenAddAnimationPicker(int layerIndex, VisualElement anchor)
        {
            VisualElement host = anchor.panel != null ? anchor.panel.visualTree : (VisualElement)this;
            VocabularyPicker.Open(
                host,
                anchor,
                VocabularyRegistryProvider.AnimationNames,
                VocabularyRegistryProvider.AnimationNames,
                VocabularyPickerConfig.ForAnimationNames(VocabularyRegistryProvider.AnimationNames),
                pickedAnimationKey => AddAnimationToLayer(layerIndex, pickedAnimationKey),
                RefreshIfChanged);
        }

        private static string ResolveAnimationDisplayName(uint animationKey)
        {
            if (animationKey == 0u)
            {
                return "(none)";
            }
            string resolvedName = VocabularyRegistryProvider.AnimationNames.FindName(animationKey);
            return resolvedName ?? "(unresolved 0x" + animationKey.ToString("X8") + ")";
        }
    }
}
