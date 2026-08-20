// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The custom inspector for <see cref="ActorAuthoring"/> (architecture section 7.1): a starting
    /// layer editor that shows layer identity honestly, plus the escape hatch to the Clip Editor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The bug this closes.</strong> <see cref="RigAsset"/>'s own doc comment is explicit
    /// that a layer's identity <em>is</em> its list position — index = priority, higher composites
    /// later — and never a name. The default inspector draws
    /// <see cref="StartingLayerState.layerIndex"/> as a bare integer field with no hint of what that
    /// number means on the actor's own rig, so authoring a starting layer is a guessing game unless
    /// the author has the rig asset open in a second inspector to count rows. This editor replaces
    /// the integer with a dropdown built from the assigned clip set's own rig, labelled
    /// <c>"index: displayName"</c>, so the number and its meaning are never separated.
    /// </para>
    /// <para>
    /// <strong>Dropdown-with-fallback, the same shape twice.</strong> Both the layer dropdown and
    /// the clip dropdown fall back to a bound, unverified field when the data needed to populate
    /// choices is not yet available (no clip set, no rig, or a rig with no layers) — the same
    /// pattern <c>RigAssetEditor</c> uses for its bone-name dropdown. An author working before a
    /// clip set is assigned still has a working field, just not a verified one.
    /// </para>
    /// <para>
    /// UI Toolkit only, per section 7 and enforced by
    /// <c>PackagingConformanceTests.Conformance_E_NoImguiApis_InEditorSources</c>: this type
    /// overrides <see cref="UnityEditor.Editor.CreateInspectorGUI"/> and never the immediate-mode
    /// entry point.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(ActorAuthoring))]
    public sealed class ActorAuthoringEditor : UnityEditor.Editor
    {
        private const string NoClipChoiceLabel = "(none)";

        private SerializedProperty clipSetProperty;
        private SerializedProperty startingLayersProperty;
        private SerializedProperty sampleOverrideProperty;
        private SerializedProperty addDistanceLodProperty;
        private SerializedProperty billboardModeProperty;
        private SerializedProperty frozenYawDegreesProperty;

        private VisualElement rigInfoSection;
        private VisualElement startingLayerRowContainer;
        private PropertyField frozenYawField;

        private readonly List<StartingLayerRowElements> startingLayerRows = new List<StartingLayerRowElements>();

        // Rebuild triggers, same reasoning as RigAssetEditor: only tear down and rebuild the dynamic
        // sections when their shape changed (a different clip set assigned, or a row added/removed),
        // never on every keystroke inside an unrelated field.
        private ClipSetAsset builtClipSet;
        private int builtStartingLayerCount;

        /// <inheritdoc />
        public override VisualElement CreateInspectorGUI()
        {
            clipSetProperty = serializedObject.FindProperty("clipSet");
            startingLayersProperty = serializedObject.FindProperty("startingLayers");
            sampleOverrideProperty = serializedObject.FindProperty("sampleOverride");
            addDistanceLodProperty = serializedObject.FindProperty("addDistanceLod");
            billboardModeProperty = serializedObject.FindProperty("billboardMode");
            frozenYawDegreesProperty = serializedObject.FindProperty("frozenYawDegrees");

            VisualElement inspectorRoot = new VisualElement();
            inspectorRoot.style.paddingTop = 4f;

            inspectorRoot.Add(BuildSectionHeading("Clip Set"));
            if (clipSetProperty != null)
            {
                inspectorRoot.Add(new PropertyField(clipSetProperty, "Clip Set"));
            }

            rigInfoSection = new VisualElement();
            inspectorRoot.Add(rigInfoSection);

            inspectorRoot.Add(BuildSectionHeading("Starting Layers"));
            startingLayerRowContainer = new VisualElement();
            inspectorRoot.Add(startingLayerRowContainer);
            Button addStartingLayerButton = new Button(AddStartingLayer) { text = "Add Starting Layer" };
            addStartingLayerButton.style.marginTop = 6f;
            inspectorRoot.Add(addStartingLayerButton);

            inspectorRoot.Add(BuildSectionHeading("Sampling & LOD"));
            inspectorRoot.Add(BuildSampleRateField());
            if (addDistanceLodProperty != null)
            {
                inspectorRoot.Add(new PropertyField(addDistanceLodProperty, "Add Distance Lod"));
            }

            inspectorRoot.Add(BuildSectionHeading("Billboard"));
            if (billboardModeProperty != null)
            {
                PropertyField billboardModeField = new PropertyField(billboardModeProperty, "Billboard Mode");
                billboardModeField.RegisterValueChangeCallback(changeEvent => RefreshFrozenYawVisibility());
                inspectorRoot.Add(billboardModeField);
            }
            if (frozenYawDegreesProperty != null)
            {
                frozenYawField = new PropertyField(frozenYawDegreesProperty, "Frozen Yaw Degrees");
                inspectorRoot.Add(frozenYawField);
            }

            inspectorRoot.Add(BuildSectionHeading("Clip Editor"));
            Button openClipEditorButton = new Button(ClipEditorWindow.ShowWindow) { text = "Open in Clip Editor" };
            openClipEditorButton.tooltip =
                "Opens the Clip Editor window. It opens blank — assign this actor's clip set inside "
                + "the window to edit its clips.";
            inspectorRoot.Add(openClipEditorButton);

            RebuildRigInfo();
            RebuildStartingLayerRows();

            // One tracked callback for the whole asset, not one per field: a starting layer row's
            // warnings depend on both the clip set and the rig it names, so there is no field whose
            // change is guaranteed to be local (same reasoning as RigAssetEditor).
            inspectorRoot.TrackSerializedObjectValue(serializedObject, OnSerializedObjectChanged);

            inspectorRoot.Bind(serializedObject);

            RefreshFrozenYawVisibility();

            return inspectorRoot;
        }

        // -----------------------------------------------------------------------------------
        // Static chrome.
        // -----------------------------------------------------------------------------------

        private static Label BuildSectionHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 10f;
            heading.style.marginBottom = 2f;
            return heading;
        }

        /// <summary>
        /// Binds only <c>sampleOverride.rateHz</c>, never the whole <c>SampleSettings</c> struct.
        /// </summary>
        /// <remarks>
        /// <see cref="ActorAuthoring.sampleOverride"/>'s own doc comment says <c>phase01</c> "is not
        /// authored" — it is a per-instance value derived at bake and re-derived at spawn (section
        /// 5.6). Drawing it as an ordinary bound field would offer an author a control that looks
        /// meaningful and is silently discarded the moment <c>RigBindingSystem</c> re-derives it, so
        /// this inspector exposes only the field the bake actually reads.
        /// </remarks>
        private VisualElement BuildSampleRateField()
        {
            if (sampleOverrideProperty == null)
            {
                return new VisualElement();
            }
            SerializedProperty rateHzProperty = sampleOverrideProperty.FindPropertyRelative("rateHz");
            if (rateHzProperty == null)
            {
                return new VisualElement();
            }
            PropertyField sampleRateField = new PropertyField(rateHzProperty, "Sample Rate (Hz)");
            sampleRateField.tooltip =
                "0 falls back to AnimationToolkitConfig.defaultSampleRateHz. The override's phase01 "
                + "is not shown here — it is derived per instance at bake and re-derived at spawn, "
                + "never authored (architecture section 5.6).";
            return sampleRateField;
        }

        // -----------------------------------------------------------------------------------
        // Billboard.
        // -----------------------------------------------------------------------------------

        private void RefreshFrozenYawVisibility()
        {
            if (frozenYawField == null || billboardModeProperty == null)
            {
                return;
            }
            bool isFrozenYaw = billboardModeProperty.enumValueIndex == (int)BillboardMode.FrozenYaw;
            frozenYawField.style.display = isFrozenYaw ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // -----------------------------------------------------------------------------------
        // Rig info.
        // -----------------------------------------------------------------------------------

        private void RebuildRigInfo()
        {
            rigInfoSection.Clear();
            builtClipSet = clipSetProperty != null ? clipSetProperty.objectReferenceValue as ClipSetAsset : null;

            if (builtClipSet == null)
            {
                rigInfoSection.Add(new HelpBox(
                    "No clip set assigned. This actor bakes to nothing until one is set — ActorBaker "
                    + "logs an error naming this GameObject.",
                    HelpBoxMessageType.Warning));
                return;
            }

            RigAsset rig = builtClipSet.rig;
            if (rig == null)
            {
                rigInfoSection.Add(new HelpBox(
                    "Clip set '" + builtClipSet.name + "' has no rig assigned. Assign a Rig Asset to "
                    + "the set before this actor can bake.",
                    HelpBoxMessageType.Warning));
                return;
            }

            Label rigLabel = new Label("Rig:  " + rig.name);
            rigLabel.style.marginBottom = 2f;
            rigInfoSection.Add(rigLabel);

            Label layerListNote = new Label(
                "Layer identity is list position, not a name — index 0 composites first, a higher "
                + "index composites later and wins (RigAsset layer contract).");
            layerListNote.style.whiteSpace = WhiteSpace.Normal;
            layerListNote.style.opacity = 0.7f;
            layerListNote.style.marginBottom = 4f;
            rigInfoSection.Add(layerListNote);

            List<LayerDefinition> layers = rig.layers;
            if (layers == null || layers.Count == 0)
            {
                rigInfoSection.Add(new Label("This rig defines no layers."));
                return;
            }
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                LayerDefinition layerDefinition = layers[layerIndex];
                string displayName = layerDefinition != null && !string.IsNullOrEmpty(layerDefinition.displayName)
                    ? layerDefinition.displayName
                    : "(unnamed)";
                bool defaultActive = layerDefinition != null && layerDefinition.defaultActive;
                rigInfoSection.Add(new Label(
                    layerIndex.ToString() + ":  " + displayName
                    + (defaultActive ? "   (active by default)" : string.Empty)));
            }
        }

        // -----------------------------------------------------------------------------------
        // Starting layer rows.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Tears down and rebuilds every starting-layer row from the current serialized array.
        /// </summary>
        /// <remarks>
        /// A full rebuild rather than an incremental diff, for the same reason
        /// <c>RigAssetEditor.RebuildSocketRows</c> gives: every row caches
        /// <see cref="SerializedProperty"/> handles into the array, and an insert or delete
        /// re-points every handle after the edit site.
        /// </remarks>
        private void RebuildStartingLayerRows()
        {
            startingLayerRows.Clear();
            startingLayerRowContainer.Clear();

            if (startingLayersProperty == null)
            {
                builtStartingLayerCount = 0;
                return;
            }

            builtStartingLayerCount = startingLayersProperty.arraySize;

            if (builtStartingLayerCount == 0)
            {
                Label emptyNote = new Label("No starting layers seeded. Every layer starts on no clip.");
                emptyNote.style.whiteSpace = WhiteSpace.Normal;
                emptyNote.style.opacity = 0.7f;
                startingLayerRowContainer.Add(emptyNote);
                return;
            }

            for (int entryIndex = 0; entryIndex < builtStartingLayerCount; entryIndex++)
            {
                StartingLayerRowElements row = BuildStartingLayerRow(entryIndex);
                startingLayerRows.Add(row);
                startingLayerRowContainer.Add(row.container);
            }

            // The rows were created after the root was bound (on every rebuild after the first), so
            // they carry no bindings yet without this call.
            startingLayerRowContainer.Bind(serializedObject);
            RefreshAllStartingLayerRows();
        }

        private StartingLayerRowElements BuildStartingLayerRow(int entryIndex)
        {
            SerializedProperty entryProperty = startingLayersProperty.GetArrayElementAtIndex(entryIndex);

            StartingLayerRowElements row = new StartingLayerRowElements();
            row.entryIndex = entryIndex;
            row.owningEditor = this;
            row.layerIndexProperty = entryProperty.FindPropertyRelative("layerIndex");
            row.clipProperty = entryProperty.FindPropertyRelative("clip");
            row.speedProperty = entryProperty.FindPropertyRelative("speed");
            row.loopProperty = entryProperty.FindPropertyRelative("loop");

            row.container = new VisualElement();
            row.container.style.marginTop = 6f;
            row.container.style.paddingLeft = 6f;
            row.container.style.paddingRight = 6f;
            row.container.style.paddingTop = 4f;
            row.container.style.paddingBottom = 6f;
            row.container.style.borderLeftWidth = 2f;
            row.container.style.borderLeftColor = new StyleColor(new Color(0.4f, 0.5f, 0.6f));

            row.layerDropdown = new DropdownField("Layer");
            row.layerDropdown.tooltip =
                "Which playback layer this entry seeds, by list position — the rig's own layer "
                + "index, not a name.";
            row.layerDropdown.RegisterValueChangedCallback(
                changeEvent => OnLayerChoiceChanged(row, changeEvent.newValue));
            row.container.Add(row.layerDropdown);

            row.layerIndexFallbackField = new PropertyField(row.layerIndexProperty, "Layer Index (raw)");
            row.layerIndexFallbackField.tooltip =
                "No clip set with a rig is assigned yet, so layer names are unknown. Raw index only.";
            row.container.Add(row.layerIndexFallbackField);

            row.clipDropdown = new DropdownField("Clip");
            row.clipDropdown.tooltip = "The clip this layer starts on, offered from the actor's own clip set.";
            row.clipDropdown.RegisterValueChangedCallback(
                changeEvent => OnClipChoiceChanged(row, changeEvent.newValue));
            row.container.Add(row.clipDropdown);

            row.clipFallbackField = new PropertyField(row.clipProperty, "Clip");
            row.container.Add(row.clipFallbackField);

            row.container.Add(new PropertyField(row.speedProperty, "Speed"));
            row.container.Add(new PropertyField(row.loopProperty, "Loop"));

            row.warningBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            row.warningBox.style.marginTop = 4f;
            row.container.Add(row.warningBox);

            int capturedIndex = entryIndex;
            Button removeButton = new Button(() => RemoveStartingLayer(capturedIndex)) { text = "Remove" };
            removeButton.style.marginTop = 4f;
            row.container.Add(removeButton);

            return row;
        }

        // -----------------------------------------------------------------------------------
        // Add and remove.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Appends a starting-layer entry with explicit, safe defaults.
        /// </summary>
        /// <remarks>
        /// Written explicitly rather than left to <see cref="SerializedProperty.InsertArrayElementAtIndex"/>'s
        /// "duplicate the previous element" behaviour — the same quirk <c>RigAssetEditor.AddSocket</c>
        /// works around for identity fields. A starting layer carries no id to duplicate, but a
        /// duplicated <c>clip</c> reference would silently seed a new layer with someone else's
        /// clip, which reads as intentional and is not.
        /// </remarks>
        private void AddStartingLayer()
        {
            if (startingLayersProperty == null)
            {
                return;
            }

            serializedObject.Update();

            int newEntryIndex = startingLayersProperty.arraySize;
            startingLayersProperty.InsertArrayElementAtIndex(newEntryIndex);
            SerializedProperty newEntry = startingLayersProperty.GetArrayElementAtIndex(newEntryIndex);

            newEntry.FindPropertyRelative("layerIndex").intValue = 0;
            newEntry.FindPropertyRelative("clip").objectReferenceValue = null;
            newEntry.FindPropertyRelative("speed").floatValue = 1f;
            newEntry.FindPropertyRelative("loop").enumValueIndex = (int)LoopMode.UseClipDefault;

            serializedObject.ApplyModifiedProperties();
            RebuildStartingLayerRows();
        }

        private void RemoveStartingLayer(int entryIndex)
        {
            if (startingLayersProperty == null
                || entryIndex < 0
                || entryIndex >= startingLayersProperty.arraySize)
            {
                return;
            }

            serializedObject.Update();
            startingLayersProperty.DeleteArrayElementAtIndex(entryIndex);
            serializedObject.ApplyModifiedProperties();
            RebuildStartingLayerRows();
        }

        // -----------------------------------------------------------------------------------
        // Dropdown writes.
        // -----------------------------------------------------------------------------------

        private void OnLayerChoiceChanged(StartingLayerRowElements row, string chosenLabel)
        {
            if (row.isRefreshing || row.layerIndexProperty == null)
            {
                return;
            }
            int chosenIndex = row.layerChoiceLabels.IndexOf(chosenLabel);
            if (chosenIndex < 0 || chosenIndex >= row.layerChoiceIndices.Count)
            {
                return;
            }

            serializedObject.Update();
            row.layerIndexProperty.intValue = row.layerChoiceIndices[chosenIndex];
            serializedObject.ApplyModifiedProperties();
            RefreshAllStartingLayerRows();
        }

        private void OnClipChoiceChanged(StartingLayerRowElements row, string chosenLabel)
        {
            if (row.isRefreshing || row.clipProperty == null)
            {
                return;
            }
            int chosenIndex = row.clipChoiceLabels.IndexOf(chosenLabel);
            if (chosenIndex < 0 || chosenIndex >= row.clipChoices.Count)
            {
                return;
            }

            serializedObject.Update();
            row.clipProperty.objectReferenceValue = row.clipChoices[chosenIndex];
            serializedObject.ApplyModifiedProperties();
            RefreshAllStartingLayerRows();
        }

        // -----------------------------------------------------------------------------------
        // Refresh.
        // -----------------------------------------------------------------------------------

        private void OnSerializedObjectChanged(SerializedObject changedSerializedObject)
        {
            if (startingLayersProperty == null)
            {
                return;
            }

            ClipSetAsset currentClipSet =
                clipSetProperty != null ? clipSetProperty.objectReferenceValue as ClipSetAsset : null;
            bool clipSetChanged = currentClipSet != builtClipSet;
            bool startingLayerCountChanged = startingLayersProperty.arraySize != builtStartingLayerCount;

            if (clipSetChanged)
            {
                RebuildRigInfo();
            }
            if (clipSetChanged || startingLayerCountChanged)
            {
                RebuildStartingLayerRows();
                return;
            }
            RefreshAllStartingLayerRows();
        }

        private void RefreshAllStartingLayerRows()
        {
            for (int rowIndex = 0; rowIndex < startingLayerRows.Count; rowIndex++)
            {
                RefreshStartingLayerRow(startingLayerRows[rowIndex]);
            }
        }

        /// <summary>
        /// Re-derives one row's dropdown-vs-fallback visibility, dropdown contents, and warnings.
        /// </summary>
        private void RefreshStartingLayerRow(StartingLayerRowElements row)
        {
            if (row == null)
            {
                return;
            }

            row.isRefreshing = true;
            try
            {
                RigAsset rig = builtClipSet != null ? builtClipSet.rig : null;
                List<LayerDefinition> rigLayers = rig != null ? rig.layers : null;
                bool hasKnownLayers = rigLayers != null && rigLayers.Count > 0;

                row.layerDropdown.style.display = hasKnownLayers ? DisplayStyle.Flex : DisplayStyle.None;
                row.layerIndexFallbackField.style.display = hasKnownLayers ? DisplayStyle.None : DisplayStyle.Flex;
                if (hasKnownLayers)
                {
                    RefreshLayerDropdown(row, rigLayers);
                }

                bool hasClipChoices = builtClipSet != null
                    && builtClipSet.clips != null
                    && builtClipSet.clips.Count > 0;
                row.clipDropdown.style.display = hasClipChoices ? DisplayStyle.Flex : DisplayStyle.None;
                row.clipFallbackField.style.display = hasClipChoices ? DisplayStyle.None : DisplayStyle.Flex;
                if (hasClipChoices)
                {
                    RefreshClipDropdown(row);
                }

                RefreshStartingLayerWarnings(row, rig);
            }
            finally
            {
                row.isRefreshing = false;
            }
        }

        private static void RefreshLayerDropdown(StartingLayerRowElements row, List<LayerDefinition> rigLayers)
        {
            row.layerChoiceLabels.Clear();
            row.layerChoiceIndices.Clear();

            for (int layerIndex = 0; layerIndex < rigLayers.Count; layerIndex++)
            {
                LayerDefinition layerDefinition = rigLayers[layerIndex];
                string displayName = layerDefinition != null && !string.IsNullOrEmpty(layerDefinition.displayName)
                    ? layerDefinition.displayName
                    : "(unnamed)";
                row.layerChoiceLabels.Add(layerIndex.ToString() + ": " + displayName);
                row.layerChoiceIndices.Add(layerIndex);
            }

            int storedLayerIndex = row.layerIndexProperty != null ? row.layerIndexProperty.intValue : 0;
            int selectedChoice = row.layerChoiceIndices.IndexOf(storedLayerIndex);
            if (selectedChoice < 0)
            {
                // The stored index names no layer on this rig. Shown as its own entry rather than
                // silently snapped to layer 0, which would destroy the evidence of the mismatch on
                // the next save (same reasoning as RigAssetEditor's "(missing target ...)" entry).
                row.layerChoiceLabels.Add("(missing layer " + storedLayerIndex.ToString() + ")");
                row.layerChoiceIndices.Add(storedLayerIndex);
                selectedChoice = row.layerChoiceLabels.Count - 1;
            }

            row.layerDropdown.choices = row.layerChoiceLabels;
            row.layerDropdown.SetValueWithoutNotify(row.layerChoiceLabels[selectedChoice]);
        }

        private static void RefreshClipDropdown(StartingLayerRowElements row)
        {
            row.clipChoiceLabels.Clear();
            row.clipChoices.Clear();

            row.clipChoiceLabels.Add(NoClipChoiceLabel);
            row.clipChoices.Add(null);

            List<ClipAsset> clips = row.OwningClipSetClips;
            for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                ClipAsset clip = clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }
                row.clipChoiceLabels.Add(clip.name);
                row.clipChoices.Add(clip);
            }

            ClipAsset storedClip = row.clipProperty != null ? row.clipProperty.objectReferenceValue as ClipAsset : null;
            int selectedChoice = row.clipChoices.IndexOf(storedClip);
            if (selectedChoice < 0)
            {
                if (storedClip != null)
                {
                    row.clipChoiceLabels.Add("(not in set) " + storedClip.name);
                    row.clipChoices.Add(storedClip);
                    selectedChoice = row.clipChoiceLabels.Count - 1;
                }
                else
                {
                    selectedChoice = 0;
                }
            }

            row.clipDropdown.choices = row.clipChoiceLabels;
            row.clipDropdown.SetValueWithoutNotify(row.clipChoiceLabels[selectedChoice]);
        }

        /// <summary>
        /// Fills in one row's inline warning box.
        /// </summary>
        /// <remarks>
        /// Both conditions mirror exactly what <c>ActorBaker.SeedStartingLayers</c> checks and logs:
        /// an out-of-range layer index or an unresolved clip makes the baker ignore the entry and
        /// print an error naming this actor. Surfacing the same rule here, at authoring time, is the
        /// one-source-of-truth requirement architecture section 7.6 sets for validation.
        /// </remarks>
        private static void RefreshStartingLayerWarnings(StartingLayerRowElements row, RigAsset rig)
        {
            List<string> messages = new List<string>();

            int layerIndex = row.layerIndexProperty != null ? row.layerIndexProperty.intValue : 0;
            if (layerIndex < 0)
            {
                messages.Add("Layer index cannot be negative. ActorBaker ignores this entry.");
            }
            else if (rig != null && rig.layers != null && layerIndex >= rig.layers.Count)
            {
                messages.Add(
                    "Layer index " + layerIndex.ToString() + " is out of range — '" + rig.name
                    + "' defines only " + rig.layers.Count.ToString()
                    + " layer(s). ActorBaker ignores this entry and logs an error naming this actor.");
            }

            ClipAsset assignedClip = row.clipProperty != null ? row.clipProperty.objectReferenceValue as ClipAsset : null;
            if (assignedClip != null)
            {
                List<ClipAsset> setClips = row.OwningClipSetClips;
                if (!setClips.Contains(assignedClip))
                {
                    string setName = row.OwningClipSetName;
                    messages.Add(
                        "Clip '" + assignedClip.name + "' is not a member of clip set '" + setName
                        + "'. ActorBaker ignores this entry and logs an error naming this actor.");
                }
            }

            if (messages.Count == 0)
            {
                row.warningBox.style.display = DisplayStyle.None;
                return;
            }
            row.warningBox.style.display = DisplayStyle.Flex;
            row.warningBox.text = string.Join("\n\n", messages.ToArray());
        }

        /// <summary>
        /// The visual elements and serialized handles of one starting-layer row.
        /// </summary>
        /// <remarks>
        /// A row owns its <see cref="SerializedProperty"/> handles so refreshes never re-walk the
        /// array by path, and reaches back to the owning editor's <see cref="builtClipSet"/> for the
        /// data its dropdowns are built from — the same shape <c>RigAssetEditor.SocketRowElements</c>
        /// uses. The handles are only valid while the array's shape is unchanged, which is why
        /// <see cref="RebuildStartingLayerRows"/> discards every row whenever an element is inserted
        /// or deleted.
        /// </remarks>
        private sealed class StartingLayerRowElements
        {
            public int entryIndex;

            public SerializedProperty layerIndexProperty;
            public SerializedProperty clipProperty;
            public SerializedProperty speedProperty;
            public SerializedProperty loopProperty;

            public VisualElement container;
            public DropdownField layerDropdown;
            public PropertyField layerIndexFallbackField;
            public DropdownField clipDropdown;
            public PropertyField clipFallbackField;
            public HelpBox warningBox;

            public readonly List<string> layerChoiceLabels = new List<string>();
            public readonly List<int> layerChoiceIndices = new List<int>();
            public readonly List<string> clipChoiceLabels = new List<string>();
            public readonly List<ClipAsset> clipChoices = new List<ClipAsset>();

            // The owning editor instance, so a row can read the current clip set's clips without
            // each row keeping its own stale copy.
            public ActorAuthoringEditor owningEditor;

            // Set while the row is being written to from code, so a dropdown's change callback can
            // tell a user's click apart from the editor's own refresh and not write back a value it
            // just read.
            public bool isRefreshing;

            public List<ClipAsset> OwningClipSetClips
            {
                get
                {
                    if (owningEditor == null || owningEditor.builtClipSet == null || owningEditor.builtClipSet.clips == null)
                    {
                        return EmptyClipList;
                    }
                    return owningEditor.builtClipSet.clips;
                }
            }

            public string OwningClipSetName
            {
                get { return owningEditor != null && owningEditor.builtClipSet != null ? owningEditor.builtClipSet.name : "(none)"; }
            }

            private static readonly List<ClipAsset> EmptyClipList = new List<ClipAsset>();
        }
    }
}
