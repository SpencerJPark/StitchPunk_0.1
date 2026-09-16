// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Capture tab: pick a source, frame it, and render a PNG sequence or a GIF.</summary>
    public sealed class CapturePanel : VisualElement, IDisposable
    {
        private const string SourceKindPrefsKey = "DotsAnimationToolkit.Capture.SourceKind";

        private ActiveAssetSelection selection;
        private CaptureSettings settings;
        private readonly FrameCaptureRunner runner = new FrameCaptureRunner();
        private ICaptureSource activeSource;
        private int sourceKindIndex;
        private bool isDisposed;

        private readonly List<uint> currentAnimationKeys = new List<uint>();

        private DropdownField sourceKindField;
        private VisualElement clipSourceRow;
        private VisualElement profileSourceRow;
        private VisualElement cutsceneSourceRow;

        private ObjectField clipSetField;
        private DropdownField clipField;
        private ObjectField rigField;

        private ObjectField profileField;
        private DropdownField animationField;
        private EnumField facingField;

        private ObjectField cutsceneField;

        private CaptureViewportElement viewport;
        private Slider previewTimeSlider;

        private IntegerField widthField;
        private IntegerField heightField;
        private DropdownField presetField;
        private IntegerField fpsField;
        private MinMaxSlider rangeSlider;
        private Label rangeSummaryLabel;
        private RadioButtonGroup backgroundGroup;
        private ColorField backgroundColourField;
        private RadioButtonGroup formatGroup;
        private TextField nameField;
        private Label effectiveNameLabel;
        private TextField outputFolderField;
        private Label resolvedFolderLabel;
        private Button captureButton;
        private Button cancelButton;
        private ProgressBar progressBar;
        private Label resultLabel;

        public CapturePanel()
        {
            style.flexGrow = 1f;
            settings = CaptureSettings.LoadFromEditorPrefs();
            sourceKindIndex = EditorPrefs.GetInt(SourceKindPrefsKey, 0);

            Add(BuildSourceRow());

            CoverPaneSplitView splitView = new CoverPaneSplitView("Capture.Settings", 1, 360f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            Add(splitView);
            splitView.Add(BuildViewportColumn());
            splitView.Add(BuildSettingsColumn());

            UpdateSourceRowVisibility();
            RefreshClipChoices(null);
            RefreshAnimationChoices(null);
            WriteSettingsIntoFieldsWithoutNotify();
            RebuildSource();
        }

        private VisualElement BuildSourceRow()
        {
            VisualElement container = ToolkitChrome.MakeAssetBar("capture-source-container");

            VisualElement kindRow = new VisualElement();
            kindRow.style.flexDirection = FlexDirection.Row;
            kindRow.style.flexWrap = Wrap.Wrap;
            kindRow.style.alignItems = Align.Center;
            List<string> sourceKindChoices = new List<string> { "Clip", "Profile Animation", "Cutscene" };
            sourceKindField = new DropdownField(sourceKindChoices, sourceKindIndex);
            sourceKindField.AddToClassList("toolkit-asset-bar__field");
            sourceKindField.RegisterValueChangedCallback(changeEvent =>
            {
                sourceKindIndex = sourceKindField.index;
                EditorPrefs.SetInt(SourceKindPrefsKey, sourceKindIndex);
                UpdateSourceRowVisibility();
                RebuildSource();
            });
            kindRow.Add(MakeSourceFieldPair("Source", sourceKindField));
            container.Add(kindRow);

            clipSourceRow = BuildClipSourceRow();
            profileSourceRow = BuildProfileSourceRow();
            cutsceneSourceRow = BuildCutsceneSourceRow();
            container.Add(clipSourceRow);
            container.Add(profileSourceRow);
            container.Add(cutsceneSourceRow);

            return container;
        }

        private VisualElement BuildClipSourceRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;

            clipSetField = new ObjectField { objectType = typeof(ClipSetAsset), allowSceneObjects = false };
            clipSetField.AddToClassList("toolkit-asset-bar__field");
            clipSetField.RegisterValueChangedCallback(changeEvent =>
            {
                ClipSetAsset newClipSet = changeEvent.newValue as ClipSetAsset;
                if (selection != null)
                {
                    selection.SetClipSet(newClipSet);
                }
                else
                {
                    OnSharedClipSetChanged(newClipSet);
                }
            });
            row.Add(MakeSourceFieldPair("Clip Set", clipSetField));

            clipField = new DropdownField(new List<string> { "(no clips)" }, 0);
            clipField.AddToClassList("toolkit-asset-bar__field");
            clipField.RegisterValueChangedCallback(changeEvent => RebuildSource());
            row.Add(MakeSourceFieldPair("Clip", clipField));

            rigField = new ObjectField { objectType = typeof(RigAsset), allowSceneObjects = false };
            rigField.AddToClassList("toolkit-asset-bar__field");
            rigField.RegisterValueChangedCallback(changeEvent =>
            {
                RigAsset newRig = changeEvent.newValue as RigAsset;
                if (selection != null)
                {
                    selection.SetRig(newRig);
                }
                else
                {
                    OnSharedRigChanged(newRig);
                }
            });
            row.Add(MakeSourceFieldPair("Rig", rigField));

            return row;
        }

        private VisualElement BuildProfileSourceRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;

            profileField = new ObjectField { objectType = typeof(ActorProfileAsset), allowSceneObjects = false };
            profileField.AddToClassList("toolkit-asset-bar__field");
            profileField.RegisterValueChangedCallback(changeEvent =>
            {
                ActorProfileAsset newProfile = changeEvent.newValue as ActorProfileAsset;
                RefreshAnimationChoices(newProfile);
                RebuildSource();
            });
            row.Add(MakeSourceFieldPair("Profile", profileField));

            animationField = new DropdownField(new List<string> { "(no animations)" }, 0);
            animationField.AddToClassList("toolkit-asset-bar__field");
            animationField.RegisterValueChangedCallback(changeEvent => RebuildSource());
            row.Add(MakeSourceFieldPair("Animation", animationField));

            facingField = new EnumField(Direction.SouthEast);
            facingField.AddToClassList("toolkit-asset-bar__field");
            facingField.RegisterValueChangedCallback(changeEvent => RebuildSource());
            row.Add(MakeSourceFieldPair("Facing", facingField));

            return row;
        }

        private VisualElement BuildCutsceneSourceRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;

            cutsceneField = new ObjectField { objectType = typeof(CutsceneAsset), allowSceneObjects = false };
            cutsceneField.AddToClassList("toolkit-asset-bar__field");
            cutsceneField.RegisterValueChangedCallback(changeEvent => RebuildSource());
            row.Add(MakeSourceFieldPair("Cutscene", cutsceneField));

            return row;
        }

        // A label and its field wrap as one unit; wrapped separately, a label could end one line
        // with its field starting the next.
        private static VisualElement MakeSourceFieldPair(string labelText, VisualElement field)
        {
            VisualElement pair = new VisualElement();
            pair.style.flexDirection = FlexDirection.Row;
            pair.style.alignItems = Align.Center;
            pair.style.flexShrink = 0f;
            pair.Add(ToolkitChrome.MakeAssetBarLabel(labelText));
            pair.Add(field);
            return pair;
        }

        private void UpdateSourceRowVisibility()
        {
            clipSourceRow.style.display = sourceKindIndex == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            profileSourceRow.style.display = sourceKindIndex == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            cutsceneSourceRow.style.display = sourceKindIndex == 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement BuildViewportColumn()
        {
            VisualElement column = new VisualElement { name = "capture-viewport-column" };
            column.AddToClassList("toolkit-column");
            column.AddToClassList("toolkit-column--flush");
            column.style.flexGrow = 1f;
            column.style.minWidth = 260f;

            column.Add(ToolkitChrome.MakePaneHeader("Preview", out _, out _));

            viewport = new CaptureViewportElement();
            viewport.style.flexGrow = 1f;
            column.Add(viewport);

            VisualElement transportRow = new VisualElement();
            transportRow.AddToClassList("toolkit-transport");
            VisualElement timeGroup = new VisualElement();
            timeGroup.AddToClassList("toolkit-transport__group");
            Label timeCaption = new Label("Time");
            timeCaption.AddToClassList("toolkit-transport__caption");
            timeGroup.Add(timeCaption);
            previewTimeSlider = new Slider(0f, 1f);
            previewTimeSlider.style.width = 280f;
            previewTimeSlider.RegisterValueChangedCallback(changeEvent => { viewport.PreviewSeconds = changeEvent.newValue; });
            timeGroup.Add(previewTimeSlider);
            transportRow.Add(timeGroup);
            column.Add(transportRow);

            return column;
        }

        private VisualElement BuildSettingsColumn()
        {
            VisualElement column = new VisualElement { name = "capture-settings-column" };
            column.AddToClassList("toolkit-column");
            column.style.minWidth = 340f;

            column.Add(ToolkitChrome.MakePaneHeader("Settings", out _, out _));

            // The cards scroll and the Capture button stays pinned below them, so a short window
            // never squeezes the cards into each other.
            ScrollView cardsScrollView = new ScrollView(ScrollViewMode.Vertical) { name = "capture-settings-scroll" };
            cardsScrollView.style.flexGrow = 1f;
            cardsScrollView.style.flexShrink = 1f;
            column.Add(cardsScrollView);

            VisualElement sizeCardBody;
            cardsScrollView.Add(ToolkitChrome.MakeCard("capture-size-card", "Size", out sizeCardBody, out _));

            // Stacked, not side by side: two 112px label columns in one row left the fields no width.
            widthField = new IntegerField { value = settings.width };
            widthField.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            heightField = new IntegerField { value = settings.height };
            heightField.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            sizeCardBody.Add(ToolkitChrome.MakePropertyRow("Width", widthField, null));
            sizeCardBody.Add(ToolkitChrome.MakePropertyRow("Height", heightField, null));

            List<string> presetLabels = new List<string>();
            foreach (CaptureSizePreset preset in CaptureSettings.SizePresets)
            {
                presetLabels.Add(preset.label);
            }
            presetField = new DropdownField(presetLabels, 0);
            presetField.RegisterValueChangedCallback(changeEvent =>
            {
                int presetIndex = presetField.index;
                if (presetIndex >= 0 && presetIndex < CaptureSettings.SizePresets.Length)
                {
                    CaptureSizePreset chosenPreset = CaptureSettings.SizePresets[presetIndex];
                    widthField.SetValueWithoutNotify(chosenPreset.width);
                    heightField.SetValueWithoutNotify(chosenPreset.height);
                    OnSettingsFieldChanged();
                }
            });
            sizeCardBody.Add(ToolkitChrome.MakePropertyRow("Preset", presetField, null));

            VisualElement timingCardBody;
            cardsScrollView.Add(ToolkitChrome.MakeCard("capture-timing-card", "Timing", out timingCardBody, out _));

            fpsField = new IntegerField { value = settings.framesPerSecond };
            fpsField.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            timingCardBody.Add(ToolkitChrome.MakePropertyRow("FPS", fpsField, null));

            rangeSlider = new MinMaxSlider(settings.rangeStartNormalized, settings.rangeEndNormalized, 0f, 1f);
            rangeSlider.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            timingCardBody.Add(ToolkitChrome.MakePropertyRow("Range", rangeSlider, null));

            rangeSummaryLabel = new Label(string.Empty);
            rangeSummaryLabel.AddToClassList("toolkit-hint");
            timingCardBody.Add(rangeSummaryLabel);

            VisualElement backgroundCardBody;
            cardsScrollView.Add(ToolkitChrome.MakeCard("capture-background-card", "Background", out backgroundCardBody, out _));

            backgroundGroup = new RadioButtonGroup(string.Empty, new List<string> { "Transparent", "Colour" });
            backgroundGroup.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            backgroundCardBody.Add(backgroundGroup);

            backgroundColourField = new ColorField { value = settings.backgroundColour };
            backgroundColourField.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            backgroundCardBody.Add(ToolkitChrome.MakePropertyRow("Colour", backgroundColourField, null));

            VisualElement formatCardBody;
            cardsScrollView.Add(ToolkitChrome.MakeCard("capture-format-card", "Format", out formatCardBody, out _));

            formatGroup = new RadioButtonGroup(string.Empty, new List<string> { "PNG Sequence", "GIF" });
            formatGroup.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            formatCardBody.Add(formatGroup);

            VisualElement outputCardBody;
            cardsScrollView.Add(ToolkitChrome.MakeCard("capture-output-card", "Output", out outputCardBody, out _));

            nameField = new TextField { value = settings.captureName };
            nameField.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            outputCardBody.Add(ToolkitChrome.MakePropertyRow("Name", nameField, null));

            effectiveNameLabel = new Label(string.Empty);
            effectiveNameLabel.AddToClassList("toolkit-hint");
            outputCardBody.Add(effectiveNameLabel);

            outputFolderField = new TextField { value = settings.outputFolder };
            outputFolderField.RegisterValueChangedCallback(changeEvent => OnSettingsFieldChanged());
            outputCardBody.Add(ToolkitChrome.MakePropertyRow("Output Folder", outputFolderField, null));

            resolvedFolderLabel = new Label(string.Empty);
            resolvedFolderLabel.style.whiteSpace = WhiteSpace.Normal;
            resolvedFolderLabel.AddToClassList("toolkit-hint");
            outputCardBody.Add(resolvedFolderLabel);

            captureButton = ToolkitChrome.MakePrimaryAction(OnCaptureButtonClicked, "d_Animation.Record", "Render the frame range to disk", "Capture");
            captureButton.style.marginTop = 10f;
            column.Add(captureButton);

            progressBar = new ProgressBar();
            progressBar.style.marginTop = 6f;
            progressBar.style.display = DisplayStyle.None;
            column.Add(progressBar);

            cancelButton = ToolkitIcons.MakeIconTextButton(OnCancelButtonClicked, "d_winbtn_win_close", "Stop after the current frame; written files are kept.", "Cancel");
            cancelButton.style.marginTop = 4f;
            cancelButton.style.display = DisplayStyle.None;
            column.Add(cancelButton);

            column.Add(ToolkitChrome.MakeStatusRow(out resultLabel, out _, true));

            return column;
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection.RigChanged -= OnSharedRigChanged;
            }
            selection = sharedSelection;
            if (selection != null)
            {
                selection.ClipSetChanged += OnSharedClipSetChanged;
                selection.RigChanged += OnSharedRigChanged;
            }
            OnSharedClipSetChanged(selection?.ClipSet);
            OnSharedRigChanged(selection?.Rig);
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            clipSetField.SetValueWithoutNotify(clipSet);
            RefreshClipChoices(clipSet);
            RebuildSource();
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            rigField.SetValueWithoutNotify(rig);
            RebuildSource();
        }

        private void RefreshClipChoices(ClipSetAsset clipSet)
        {
            List<string> clipNames = new List<string>();
            if (clipSet != null && clipSet.clips != null)
            {
                foreach (ClipAsset clipAsset in clipSet.clips)
                {
                    clipNames.Add(clipAsset != null ? clipAsset.name : "(missing clip)");
                }
            }
            bool hasClips = clipNames.Count > 0;
            if (!hasClips)
            {
                clipNames.Add("(no clips)");
            }
            clipField.choices = clipNames;
            clipField.index = 0;
            clipField.SetEnabled(hasClips);
        }

        private ClipAsset ResolveSelectedClip(ClipSetAsset clipSet)
        {
            if (clipSet == null || clipSet.clips == null || clipSet.clips.Count == 0)
            {
                return null;
            }
            int selectedIndex = clipField.index;
            if (selectedIndex < 0 || selectedIndex >= clipSet.clips.Count)
            {
                selectedIndex = 0;
            }
            return clipSet.clips[selectedIndex];
        }

        private void RefreshAnimationChoices(ActorProfileAsset profile)
        {
            currentAnimationKeys.Clear();
            List<string> animationNames = new List<string>();
            if (profile != null && profile.layers != null)
            {
                AnimationNameRegistry registry = VocabularyRegistryProvider.AnimationNames;
                foreach (ActorLayerDefinition layer in profile.layers)
                {
                    if (layer.animations == null)
                    {
                        continue;
                    }
                    foreach (ActorAnimationDefinition animationDefinition in layer.animations)
                    {
                        if (currentAnimationKeys.Contains(animationDefinition.animationKey))
                        {
                            continue;
                        }
                        currentAnimationKeys.Add(animationDefinition.animationKey);
                        string resolvedName = registry != null ? registry.FindName(animationDefinition.animationKey) : null;
                        animationNames.Add(resolvedName != null ? resolvedName : "(unresolved 0x" + animationDefinition.animationKey.ToString("X8") + ")");
                    }
                }
            }
            bool hasAnimations = animationNames.Count > 0;
            if (!hasAnimations)
            {
                animationNames.Add("(no animations)");
            }
            animationField.choices = animationNames;
            animationField.index = 0;
            animationField.SetEnabled(hasAnimations);
        }

        private uint ResolveSelectedAnimationKey(ActorProfileAsset profile)
        {
            if (profile == null || currentAnimationKeys.Count == 0)
            {
                return 0u;
            }
            int selectedIndex = animationField.index;
            if (selectedIndex < 0 || selectedIndex >= currentAnimationKeys.Count)
            {
                selectedIndex = 0;
            }
            return currentAnimationKeys[selectedIndex];
        }

        private void RebuildSource()
        {
            if (viewport == null)
            {
                return;
            }

            ICaptureSource newSource = null;
            if (sourceKindIndex == 0)
            {
                ClipSetAsset clipSet = clipSetField.value as ClipSetAsset;
                RigAsset rig = rigField.value as RigAsset;
                ClipAsset clip = ResolveSelectedClip(clipSet);
                if (clipSet != null && rig != null && clip != null)
                {
                    newSource = new ClipCaptureSource(clipSet, rig, clip);
                }
            }
            else if (sourceKindIndex == 1)
            {
                ActorProfileAsset profile = profileField.value as ActorProfileAsset;
                if (profile != null && currentAnimationKeys.Count > 0)
                {
                    uint animationKey = ResolveSelectedAnimationKey(profile);
                    Direction facing = (Direction)facingField.value;
                    newSource = new ProfileAnimationCaptureSource(profile, animationKey, facing);
                }
            }
            else
            {
                CutsceneAsset cutscene = cutsceneField.value as CutsceneAsset;
                if (cutscene != null)
                {
                    newSource = new CutsceneCaptureSource(cutscene);
                }
            }

            if (activeSource != null)
            {
                CaptureSettings.SaveCameraPose(activeSource.CameraPoseKey, activeSource.CaptureCameraPose());
                activeSource.Dispose();
            }

            activeSource = newSource;
            if (activeSource != null)
            {
                PreviewCameraPose restoredPose;
                if (CaptureSettings.TryLoadCameraPose(activeSource.CameraPoseKey, out restoredPose))
                {
                    activeSource.RestoreCameraPose(in restoredPose);
                }
            }

            viewport.Source = activeSource;
            RefreshPreviewTimeRange();
            RefreshEffectiveNameLabel();
            RefreshRangeSummaryLabel();
        }

        private void RefreshPreviewTimeRange()
        {
            float durationSeconds = activeSource != null ? Mathf.Max(0.01f, activeSource.DurationSeconds) : 1f;
            previewTimeSlider.highValue = durationSeconds;
            if (previewTimeSlider.value > durationSeconds)
            {
                previewTimeSlider.SetValueWithoutNotify(durationSeconds);
            }
            previewTimeSlider.SetEnabled(activeSource != null);
        }

        private void OnSettingsFieldChanged()
        {
            ReadFieldsIntoSettings();
            settings.ClampToValidRanges();
            WriteSettingsIntoFieldsWithoutNotify();
            settings.SaveToEditorPrefs();
            viewport.ApplySettings(settings);
            RefreshRangeSummaryLabel();
            RefreshResolvedFolderLabel();
            RefreshEffectiveNameLabel();
        }

        private void ReadFieldsIntoSettings()
        {
            settings.width = widthField.value;
            settings.height = heightField.value;
            settings.framesPerSecond = fpsField.value;
            settings.rangeStartNormalized = rangeSlider.value.x;
            settings.rangeEndNormalized = rangeSlider.value.y;
            settings.background = backgroundGroup.value == 1 ? CaptureBackgroundMode.SolidColour : CaptureBackgroundMode.Transparent;
            settings.backgroundColour = backgroundColourField.value;
            settings.format = formatGroup.value == 1 ? CaptureOutputFormat.Gif : CaptureOutputFormat.PngSequence;
            settings.captureName = nameField.value ?? string.Empty;
            settings.outputFolder = outputFolderField.value ?? string.Empty;
        }

        private void WriteSettingsIntoFieldsWithoutNotify()
        {
            widthField.SetValueWithoutNotify(settings.width);
            heightField.SetValueWithoutNotify(settings.height);
            fpsField.SetValueWithoutNotify(settings.framesPerSecond);
            rangeSlider.SetValueWithoutNotify(new Vector2(settings.rangeStartNormalized, settings.rangeEndNormalized));
            backgroundGroup.SetValueWithoutNotify(settings.background == CaptureBackgroundMode.SolidColour ? 1 : 0);
            backgroundColourField.SetValueWithoutNotify(settings.backgroundColour);
            backgroundColourField.SetEnabled(settings.background == CaptureBackgroundMode.SolidColour);
            formatGroup.SetValueWithoutNotify(settings.format == CaptureOutputFormat.Gif ? 1 : 0);
            nameField.SetValueWithoutNotify(settings.captureName);
            outputFolderField.SetValueWithoutNotify(settings.outputFolder);
            RefreshRangeSummaryLabel();
            RefreshResolvedFolderLabel();
            RefreshEffectiveNameLabel();
        }

        private void RefreshRangeSummaryLabel()
        {
            float durationSeconds = activeSource != null ? activeSource.DurationSeconds : 0f;
            float startSeconds = settings.RangeStartSeconds(durationSeconds);
            float endSeconds = settings.RangeEndSeconds(durationSeconds);
            int frameCount = settings.FrameCountFor(durationSeconds);
            rangeSummaryLabel.text = frameCount + " frames, " + startSeconds.ToString("0.00") + " s to " + endSeconds.ToString("0.00") + " s";
        }

        private void RefreshResolvedFolderLabel()
        {
            resolvedFolderLabel.text = "Writes to " + settings.ResolvedOutputFolder;
        }

        private void RefreshEffectiveNameLabel()
        {
            string effectiveName = !string.IsNullOrEmpty(settings.captureName)
                ? settings.captureName
                : (activeSource != null ? activeSource.CaptureName : "Capture");
            effectiveNameLabel.text = "Saved as " + effectiveName;
        }

        private void OnCaptureButtonClicked()
        {
            if (runner.IsRunning)
            {
                return;
            }
            if (activeSource == null)
            {
                ToolkitChrome.SetStatus(resultLabel, "No capture source selected.", ToolkitStatusTone.Error);
                return;
            }
            if (activeSource.NotReadyReason != null)
            {
                ToolkitChrome.SetStatus(resultLabel, activeSource.NotReadyReason, ToolkitStatusTone.Error);
                return;
            }

            CaptureSettings captureSettingsClone = settings.Clone();
            captureSettingsClone.ClampToValidRanges();
            if (string.IsNullOrEmpty(captureSettingsClone.captureName))
            {
                captureSettingsClone.captureName = activeSource.CaptureName;
            }

            int frameCountForRange = captureSettingsClone.FrameCountFor(activeSource.DurationSeconds);
            List<string> existingFiles = PngSequenceWriter.FindFilesThatWouldBeOverwritten(
                captureSettingsClone.ResolvedOutputFolder, captureSettingsClone.captureName, frameCountForRange, captureSettingsClone.format);

            if (existingFiles.Count > 0)
            {
                bool overwriteConfirmed = EditorUtility.DisplayDialog(
                    "Overwrite capture?",
                    existingFiles.Count + " existing files in " + captureSettingsClone.ResolvedOutputFolder + " will be replaced.",
                    "Overwrite",
                    "Cancel");
                if (!overwriteConfirmed)
                {
                    return;
                }
            }

            CaptureSettings.SaveCameraPose(activeSource.CameraPoseKey, activeSource.CaptureCameraPose());
            viewport.RenderingSuspended = true;
            captureButton.SetEnabled(false);
            cancelButton.style.display = DisplayStyle.Flex;
            progressBar.style.display = DisplayStyle.Flex;
            progressBar.value = 0f;
            progressBar.title = "Starting capture...";
            ToolkitChrome.SetStatus(resultLabel, string.Empty, ToolkitStatusTone.Neutral);

            runner.Start(
                activeSource,
                captureSettingsClone,
                (framesCaptured, totalFrameCount) =>
                {
                    progressBar.value = totalFrameCount > 0 ? framesCaptured / (float)totalFrameCount * 100f : 0f;
                    progressBar.title = "Frame " + framesCaptured + " of " + totalFrameCount;
                },
                finishedMessage =>
                {
                    viewport.RenderingSuspended = false;
                    captureButton.SetEnabled(true);
                    cancelButton.style.display = DisplayStyle.None;
                    progressBar.style.display = DisplayStyle.None;
                    ToolkitStatusTone finishedTone = finishedMessage.StartsWith("Capture failed:") ? ToolkitStatusTone.Error : ToolkitStatusTone.Neutral;
                    ToolkitChrome.SetStatus(resultLabel, finishedMessage, finishedTone);
                });
        }

        private void OnCancelButtonClicked()
        {
            runner.Cancel();
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            isDisposed = true;

            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection.RigChanged -= OnSharedRigChanged;
                selection = null;
            }
            if (runner.IsRunning)
            {
                runner.Cancel();
            }
            if (activeSource != null)
            {
                CaptureSettings.SaveCameraPose(activeSource.CameraPoseKey, activeSource.CaptureCameraPose());
                activeSource.Dispose();
                activeSource = null;
            }
            viewport?.Dispose();
        }
    }
}
