// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Inspector pane: the key, object and clip field builders and their live bindings; every edit it makes goes back through the window's undo, commit and rebuild delegates, so nothing here hand-rolls an edit path.</summary>
    public sealed class ClipInspectorPane : VisualElement, System.IDisposable
    {
        internal delegate TransformValueState ResolveDisplayedTransformHandler(
            uint targetId, out float3 position, out float3 rotationDegrees, out float3 scale);
        internal delegate void ReadRigEditPoseHandler(
            uint targetId, out float3 position, out float3 rotationDegrees, out float3 scale);

        private const string HintUssClassName = "clip-editor__hint";
        private const string HeadingUssClassName = "clip-editor__heading";
        private const string FlipbookTrackUssClassName = "clip-editor__flipbook-track";
        private const string FlipbookKeyUssClassName = "clip-editor__flipbook-key";
        private const string FlipbookResolvedUssClassName = "clip-editor__flipbook-resolved";
        private const string FlipbookInvalidUssClassName = "clip-editor__flipbook-resolved--invalid";
        private const string TransformBlockUssClassName = "clip-editor__transform-block";
        private const string TransformOnKeyUssClassName = "clip-editor__transform-block--on-key";
        private const string TransformInterpolatedUssClassName = "clip-editor__transform-block--interpolated";
        private const string TransformModifiedUssClassName = "clip-editor__transform-block--modified";
        private const string TransformStateChipUssClassName = "clip-editor__transform-state";
        private const string SelectionHeadingRowUssClassName = "clip-editor__selection-heading-row";
        private const string SelectionHeadingUssClassName = "clip-editor__selection-heading";
        private const string SelectionHeadingActiveUssClassName =
            "clip-editor__selection-heading--active";
        private const string SelectionHeadingTagButtonUssClassName =
            "clip-editor__selection-heading-tag-button";

        private ScrollView inspectorPane;
        private SerializedObject clipSerializedObject;

        private ActiveAssetSelection selection;
        private ClipEditorSession session;
        private ClipPreviewController previewController;

        // Scratch for the socket bone dropdown; CollectHierarchyNames clears it before filling.
        private readonly HashSet<string> hierarchyNameCache = new HashSet<string>();

        // Where pickers open their overlay: the window's root, not this pane's subtree.
        internal VisualElement PickerRoot { get; set; }

        // The component stack (a window partial) builds its blocks straight into this.
        internal ScrollView ContentPane { get { return inspectorPane; } }

        // Window operations the field builders call, handed in once at bind time and named after
        // the members they stand in for so the builders read as they did on the window.
        internal System.Func<bool> IsRigEditMode { get; set; }
        internal System.Action<string> RecordClipEdit { get; set; }
        internal System.Action CommitClipEdit { get; set; }
        internal System.Action RequestInspectorRebuild { get; set; }
        internal System.Action RequestTimelineRebuild { get; set; }
        internal System.Action RequestHierarchyRebuild { get; set; }
        internal System.Action RebuildTimeline { get; set; }
        internal System.Action MarkPreviewDirty { get; set; }
        internal System.Action<string> BeginUndoGesture { get; set; }
        internal System.Action EndUndoGesture { get; set; }
        internal System.Action<string> ReportStatus { get; set; }
        internal System.Func<KeyAddress, float> GetKeyTime { get; set; }
        internal System.Func<KeyAddress, int> ResolveEventFlatIndex { get; set; }
        internal System.Func<string, int> FindBoneTrackIndex { get; set; }
        internal System.Func<KeyAddress, HierarchyItem> FindHierarchyItemForKey { get; set; }
        internal System.Action<HierarchyItem, bool> BuildComponentStack { get; set; }
        internal System.Action AddSocketDirectory { get; set; }
        internal System.Action<uint> FocusSocket { get; set; }
        internal System.Action<RigAsset, string> RecordSocketEdit { get; set; }
        internal System.Action<bool> CommitSocketEdit { get; set; }
        internal System.Action CommitSocketPlacementEdit { get; set; }
        internal ResolveDisplayedTransformHandler ResolveDisplayedTransform { get; set; }
        internal ReadRigEditPoseHandler ReadRigEditPose { get; set; }
        internal System.Action<uint, float3, float3, float3, bool> ApplyTransformEdit { get; set; }
        internal System.Action CommitPendingTransformEdit { get; set; }
        internal System.Action DiscardPendingTransformEdit { get; set; }
        internal System.Action<uint, float3, float3, float3> KeyDisplayedTransform { get; set; }
        internal System.Func<uint, bool> IsTransformEditHeldFor { get; set; }

        // The clip's asset name changed in place; the clip list and the timeline both show it.
        internal event System.Action ClipRenamed;

        public void Bind(
            VisualElement paneRoot,
            ActiveAssetSelection sharedSelection,
            ClipEditorSession editorSession,
            ClipPreviewController preview)
        {
            selection = sharedSelection;
            session = editorSession;
            previewController = preview;
            if (paneRoot != null)
            {
                BindInspector(paneRoot);
            }
        }

        public void Dispose()
        {
            ClearLiveInspectorBindings();
            clipSerializedObject = null;
        }

        private RigAsset ActiveRig
        {
            get { return selection != null ? selection.Rig : null; }
        }

        private GameObject LoadedPrefab
        {
            get { return ActiveRig != null ? ActiveRig.sourcePrefab : null; }
        }

        private void BindInspector(VisualElement paneRoot)
        {
            inspectorPane = paneRoot.Q<ScrollView>("inspector-content");
        }

        internal void RefreshSerializedClip()
        {
            clipSerializedObject = session.SelectedClip != null ? new SerializedObject(session.SelectedClip) : null;
        }

        // -------------------------------------------------------------------------------------
        // Inspector. Bound fields get undo, dirtying and prefab overrides for free, so nothing
        // here hand-rolls an edit path.
        // -------------------------------------------------------------------------------------

        /// <summary>One flipbook track's live fields, so a scrub can update them without a rebuild.</summary>
        private sealed class LiveFlipbookBinding
        {
            public SpriteTrack track;
            public IntegerField valueField;
            public EnumField indexModeField;
            public Label resolvedLabel;
            public Label stateHint;
        }

        /// <summary>One selected object's transform fields, so a scrub can update them in place.</summary>
        private sealed class LiveTransformBinding
        {
            /// <summary>The rig target this block edits; 0 when <see cref="boneName"/> is set.</summary>
            public uint targetId;

            // The name, not the track: a node with no keys yet still has a block on screen, and the
            // track that will hold its poses does not exist yet either.
            /// <summary>The node this block edits by name; empty for a part.</summary>
            public string boneName;

            public VisualElement block;
            public Label stateChip;
            public Vector3Field positionField;
            public Vector3Field rotationField;
            public Vector3Field scaleField;
        }

        private readonly List<LiveTransformBinding> liveTransformBindings =
            new List<LiveTransformBinding>();
        private readonly List<LiveFlipbookBinding> liveFlipbookBindings =
            new List<LiveFlipbookBinding>();

        private void ClearLiveInspectorBindings()
        {
            liveTransformBindings.Clear();
            liveFlipbookBindings.Clear();
        }

        // A focused field is skipped, not overwritten: half-typed text is a value the user is
        // mid-authoring, and a scrub that stamped over it would fight the person using it.
        /// <summary>Pushes the value at the playhead into the fields already on screen.</summary>
        internal void RefreshLiveInspectorValues()
        {
            for (int bindingIndex = 0; bindingIndex < liveTransformBindings.Count; bindingIndex++)
            {
                RefreshLiveTransformBinding(liveTransformBindings[bindingIndex]);
            }

            for (int bindingIndex = 0; bindingIndex < liveFlipbookBindings.Count; bindingIndex++)
            {
                RefreshLiveFlipbookBinding(liveFlipbookBindings[bindingIndex]);
            }
        }

        private void RefreshLiveTransformBinding(LiveTransformBinding binding)
        {
            if (!string.IsNullOrEmpty(binding.boneName))
            {
                RefreshLiveBoneValues(binding);
                return;
            }

            // Rig Edit's fields show the live preview pose, not the clip's offset-from-rest value
            // (see AddTransformFields) -- the per-tick refresh has to keep showing that same thing,
            // or the correct value painted when the block was built would be overwritten by the
            // wrong one on the very next tick.
            if (IsRigEditMode())
            {
                RefreshLiveRigEditTransform(binding);
                return;
            }

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            TransformValueState valueState = ResolveDisplayedTransform(
                binding.targetId, out position, out rotationDegrees, out scale);

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));

            if (binding.stateChip != null)
            {
                binding.stateChip.text = DescribeTransformState(valueState);
                binding.stateChip.EnableInClassList(
                    TransformModifiedUssClassName, valueState == TransformValueState.Modified);
            }
            if (binding.block != null)
            {
                binding.block.EnableInClassList(
                    TransformOnKeyUssClassName, valueState == TransformValueState.OnKey);
                binding.block.EnableInClassList(
                    TransformInterpolatedUssClassName,
                    valueState == TransformValueState.Interpolated);
                binding.block.EnableInClassList(
                    TransformModifiedUssClassName, valueState == TransformValueState.Modified);
            }
        }

        /// <summary>
        /// Rig Edit's per-tick refresh for a rig target's transform block: the same live-pose
        /// source <see cref="AddTransformFields"/> paints it with initially, kept in sync so the
        /// fields never drift from what the viewport gizmo is dragging.
        /// </summary>
        private void RefreshLiveRigEditTransform(LiveTransformBinding binding)
        {
            float3 position;
            float3 rotationDegrees;
            float3 scale;
            ReadRigEditPose(binding.targetId, out position, out rotationDegrees, out scale);

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));
        }

        private void RefreshLiveBoneValues(LiveTransformBinding binding)
        {
            // Rig Edit's fields show the live preview pose, not a bone track's key value -- see
            // AddBoneTransformFields. The per-tick refresh has to keep showing that same thing.
            if (IsRigEditMode())
            {
                RefreshLiveRigEditBone(binding);
                return;
            }

            // Looked up per refresh rather than held, because the first key on this node mints the
            // track: a reference captured when the block was built would stay null for the rest of
            // the block's life, leaving the fields frozen the moment they started to matter.
            BoneTrack track = FindBoneTrack(binding.boneName);

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            bool hasKeys = ClipBoneEditing.TryEvaluate(
                track, session.PlayheadNormalized, out position, out rotationDegrees, out scale);
            bool isOnKey = ClipBoneEditing.FindKeyIndexAt(track, session.PlayheadNormalized) >= 0;

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));

            if (binding.stateChip != null)
            {
                binding.stateChip.text = DescribeBoneState(hasKeys, isOnKey);
            }
            if (binding.block != null)
            {
                binding.block.EnableInClassList(TransformOnKeyUssClassName, isOnKey);
                binding.block.EnableInClassList(
                    TransformInterpolatedUssClassName, hasKeys && !isOnKey);
            }
        }

        /// <summary>
        /// Rig Edit's per-tick refresh for a bone or bare grouping transform's block: the same
        /// live-pose source <see cref="AddBoneTransformFields"/> paints it with initially.
        /// </summary>
        private void RefreshLiveRigEditBone(LiveTransformBinding binding)
        {
            float3 position;
            float3 rotationDegrees;
            float3 scale;
            ReadRigEditBonePose(binding.boneName, out position, out rotationDegrees, out scale);

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));
        }

        private void RefreshLiveFlipbookBinding(LiveFlipbookBinding binding)
        {
            SpriteTrack track = binding.track;
            if (track == null || track.keys == null || track.keys.Count == 0)
            {
                return;
            }

            int effectiveKeyIndex = ClipSpriteEditing.FindEffectiveKeyIndex(track, session.PlayheadNormalized);
            if (effectiveKeyIndex < 0)
            {
                return;
            }
            SpriteKey currentKey = track.keys[effectiveKeyIndex];

            if (binding.valueField != null && !IsBeingEdited(binding.valueField))
            {
                binding.valueField.SetValueWithoutNotify(currentKey.sliceIndex);
            }
            if (binding.indexModeField != null && !IsBeingEdited(binding.indexModeField))
            {
                binding.indexModeField.SetValueWithoutNotify(currentKey.indexMode);
            }
            if (binding.resolvedLabel != null)
            {
                ApplyFlipbookResolvedLabel(binding.resolvedLabel, currentKey, track.baseIndex);
            }
            if (binding.stateHint != null)
            {
                binding.stateHint.text =
                    ClipSpriteEditing.FindKeyIndexAt(track, session.PlayheadNormalized) >= 0
                        ? "On a key — editing changes this key."
                        : "Held from an earlier key — editing keys the value here.";
            }
        }

        private static void SetVectorWithoutDisturbingEdit(Vector3Field field, Vector3 value)
        {
            if (field == null || IsBeingEdited(field))
            {
                return;
            }
            field.SetValueWithoutNotify(value);
        }

        // The capture test is not redundant with the focus test: a field's drag handle captures the
        // mouse without focusing the input behind it, so focus alone misses a number being dragged.
        /// <summary>Whether the user is currently typing in a field, or dragging it.</summary>
        internal static bool IsBeingEdited(VisualElement field)
        {
            if (field == null || field.panel == null)
            {
                return false;
            }

            VisualElement capturing =
                field.panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement;
            if (capturing != null && (capturing == field || field.Contains(capturing)))
            {
                return true;
            }

            VisualElement focused = field.panel.focusController.focusedElement as VisualElement;
            return focused != null && (focused == field || field.Contains(focused));
        }

        /// <summary>Fills the inspector for whatever is selected: a key, a bone, or the clip itself.</summary>
        internal void RebuildInspector()
        {
            if (inspectorPane == null)
            {
                return;
            }
            inspectorPane.Clear();
            ClearLiveInspectorBindings();

            if (session.SelectedKeys.Count > 0 && BuildKeyInspector())
            {
                return;
            }

            // One labelled block per selected object, in pick order. With a single selection this
            // is exactly the old panel plus a name; with several it is the only way to tell whose
            // numbers are whose.
            if (session.SelectedHierarchyItems.Count > 0)
            {
                // Only marked when there is more than one block: with a single selection every
                // block is the active one, and saying so is noise.
                HierarchyItem activeItem =
                    session.SelectedHierarchyItems.Count > 1 ? session.ActiveHierarchyItem : null;

                for (int itemIndex = 0; itemIndex < session.SelectedHierarchyItems.Count; itemIndex++)
                {
                    HierarchyItem item = session.SelectedHierarchyItems[itemIndex];
                    BuildComponentStack(item, item == activeItem);
                }
                return;
            }

            BuildClipInspector();
        }

        /// <summary>Returns false when the addressed key has gone, so the caller can fall through.</summary>
        private bool BuildKeyInspector()
        {
            if (session.SelectedClip == null || clipSerializedObject == null)
            {
                return false;
            }
            clipSerializedObject.Update();

            // Multi-select edits the last address only. Driving N keys from one field needs a
            // mixed-value story the property system does not hand us, so rather than pretend, the
            // inspector says plainly which key it is editing.
            // The clicked key, when one is known. Falling back to an arbitrary set member only
            // happens for selections made without a click, such as a box select.
            KeyAddress shown = default(KeyAddress);
            if (session.HasActiveKey && session.SelectedKeys.Contains(session.ActiveKey))
            {
                shown = session.ActiveKey;
            }
            else
            {
                foreach (KeyAddress address in session.SelectedKeys)
                {
                    shown = address;
                }
            }

            SerializedProperty keyProperty = FindKeyProperty(shown);
            if (keyProperty == null)
            {
                return false;
            }

            // The key's object first, with its components. A key is a moment of something, and the
            // something is what the channels belong to — reading the key without it meant losing
            // sight of what else the part was doing at that time. An event marker has no object:
            // it belongs to the clip, so it gets no stack.
            if (shown.trackKind != TimelineTrackKind.Event)
            {
                HierarchyItem owningItem = FindHierarchyItemForKey(shown);
                if (owningItem != null)
                {
                    BuildComponentStack(owningItem, true);
                }
            }

            inspectorPane.Add(MakeHeading(
                shown.trackKind.ToString() + " key at "
                + GetKeyTime(shown).ToString("0.###")));
            if (session.SelectedKeys.Count > 1)
            {
                inspectorPane.Add(MakeHint(
                    session.SelectedKeys.Count.ToString() + " selected — editing the last."));
            }

            // A flipbook key gets purpose-built fields rather than the generic property drawer,
            // because its stored number is only meaningful beside its mode and its track's base —
            // three fields the drawer renders as three unrelated numbers.
            if (shown.trackKind == TimelineTrackKind.Sprite)
            {
                AddSelectedFlipbookKeyFields(shown);
                return true;
            }

            // An event marker gets purpose-built fields for the same reason a flipbook key does: the
            // generic drawer renders its key as a bare uint the author has to know the meaning of,
            // and its window as a number of seconds nobody times animation in.
            if (shown.trackKind == TimelineTrackKind.Event)
            {
                AddSelectedEventMarkerFields(shown);
                return true;
            }

            AddKeyValueFields(keyProperty);
            inspectorPane.Bind(clipSerializedObject);

            AddInterpolationControls(shown);
            return true;
        }

        // Flattened rather than one PropertyField on the struct: the drawer renders an array
        // element as a foldout named "Element 3", meaningless beside a heading naming the key by
        // its time. Easing fields are skipped since AddInterpolationControls shows them as a curve.
        /// <summary>The key's own values, each as its own field, with the easing fields left out.</summary>
        private void AddKeyValueFields(SerializedProperty keyProperty)
        {
            SerializedProperty childProperty = keyProperty.Copy();
            SerializedProperty endProperty = keyProperty.GetEndProperty();
            bool enterChildren = true;
            while (childProperty.NextVisible(enterChildren)
                && !SerializedProperty.EqualContents(childProperty, endProperty))
            {
                enterChildren = false;
                if (IsEasingPropertyName(childProperty.name))
                {
                    continue;
                }
                inspectorPane.Add(new PropertyField(childProperty.Copy()));
            }
        }

        private static bool IsEasingPropertyName(string propertyName)
        {
            return propertyName == "interpolation"
                || propertyName == "bezierStartHandle"
                || propertyName == "bezierEndHandle";
        }

        // The window field edits in frames but stores seconds — the conversion happens here, at the
        // one point a person is looking at the number, with resolved seconds shown beside it.
        /// <summary>The selected event marker: which event it is, how long its window runs, and its payload.</summary>
        private void AddSelectedEventMarkerFields(KeyAddress address)
        {
            int flatIndex = ResolveEventFlatIndex(address);
            if (session.SelectedClip.events == null || flatIndex < 0)
            {
                return;
            }

            EventMarker marker = session.SelectedClip.events[flatIndex];
            AnimEventKeyRegistry registry = ResolveEventKeyRegistry();

            AddEventKeyField(address, marker, registry);
            AddEventWindowField(address, marker, registry);

            IntegerField intParamField = new IntegerField("Int Param");
            intParamField.tooltip =
                "Delivered on the AnimEventOutput pulse. Not carried by the window mask.";
            intParamField.SetValueWithoutNotify(marker.intParam);
            intParamField.RegisterValueChangedCallback(changeEvent =>
            {
                EditEventMarker(address, "Edit Event Payload", editedMarker =>
                {
                    editedMarker.intParam = changeEvent.newValue;
                    return editedMarker;
                });
            });
            inspectorPane.Add(intParamField);

            FloatField floatParamField = new FloatField("Float Param");
            floatParamField.tooltip =
                "Delivered on the AnimEventOutput pulse. Not carried by the window mask.";
            floatParamField.SetValueWithoutNotify(marker.floatParam);
            floatParamField.RegisterValueChangedCallback(changeEvent =>
            {
                EditEventMarker(address, "Edit Event Payload", editedMarker =>
                {
                    editedMarker.floatParam = changeEvent.newValue;
                    return editedMarker;
                });
            });
            inspectorPane.Add(floatParamField);
        }

        /// <summary>Which event this marker fires, chosen from the project's event-name vocabulary.</summary>
        private void AddEventKeyField(
            KeyAddress address, EventMarker marker, AnimEventKeyRegistry registry)
        {
            Button eventButton = new Button
            {
                text = "Event: " + DescribeEventName(marker.eventKey, registry)
            };
            eventButton.clicked += () => OpenEventKeyPicker(address, registry, eventButton);
            inspectorPane.Add(eventButton);
            inspectorPane.Add(MakeHint(DescribeEventKey(marker.eventKey, registry)));
        }

        /// <summary>The event's name, or an unresolved id when the registry does not (or no longer) names it.</summary>
        internal static string DescribeEventName(uint eventKey, AnimEventKeyRegistry registry)
        {
            string resolvedName = registry != null ? registry.FindName(eventKey) : null;
            return resolvedName ?? "(unresolved 0x" + eventKey.ToString("X8") + ")";
        }

        private void OpenEventKeyPicker(
            KeyAddress address, AnimEventKeyRegistry registry, Button anchor)
        {
            VocabularyPicker.Open(
                PickerRoot,
                anchor,
                registry,
                registry,
                VocabularyPickerConfig.ForEventKeys(registry),
                chosenEventKey => ApplyEventKeyChoice(address, chosenEventKey, registry),
                RebuildInspector);
        }

        private void ApplyEventKeyChoice(
            KeyAddress address, uint chosenEventKey, AnimEventKeyRegistry registry)
        {
            int flatIndex = ResolveEventFlatIndex(address);
            if (flatIndex < 0)
            {
                return;
            }

            AnimEventKeyEntry chosen = FindRegistryEntryByKey(registry, chosenEventKey);
            EditEventMarker(address, "Change Event Key", editedMarker =>
            {
                editedMarker.eventKey = chosenEventKey;

                // The registry's default window applies only when the marker has none of its own,
                // so re-pointing a hand-tuned six-frame window at another event does not quietly
                // reset it to that event's default.
                if (editedMarker.windowSeconds <= 0f && chosen != null && chosen.defaultWindowFrames > 0)
                {
                    editedMarker.windowSeconds =
                        chosen.defaultWindowFrames / ResolveReferenceFrameRate(registry);
                }
                return editedMarker;
            });

            // The eventKey just written can move the marker into a different lane (E6 Task 2), so
            // its selection has to follow — re-resolved from the flat index captured before the
            // edit rather than trusting the caller's now possibly-stale lane/local pair.
            KeyAddress newAddress = ResolveEventKeyAddressForFlatIndex(flatIndex);
            if (session.SelectedKeys.Remove(address))
            {
                session.SelectedKeys.Add(newAddress);
            }
            if (session.HasActiveKey && session.ActiveKey.Equals(address))
            {
                session.ActiveKey = newAddress;
            }

            RebuildInspector();
        }

        /// <summary>The lane-local <see cref="KeyAddress"/> for an event marker at a known flat index.</summary>
        internal KeyAddress ResolveEventKeyAddressForFlatIndex(int flatIndex)
        {
            uint eventKey = session.SelectedClip.events[flatIndex].eventKey;
            List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(session.SelectedClip.events);
            int laneIndex = laneKeys.IndexOf(eventKey);
            int localIndex = EventLaneAddressing
                .ResolveLaneFlatIndices(session.SelectedClip.events, laneIndex).IndexOf(flatIndex);
            return new KeyAddress(TimelineTrackKind.Event, laneIndex, localIndex);
        }

        /// <summary>How long the marker holds its mask bit, edited in frames.</summary>
        private void AddEventWindowField(
            KeyAddress address, EventMarker marker, AnimEventKeyRegistry registry)
        {
            float frameRate = ResolveReferenceFrameRate(registry);

            IntegerField windowField = new IntegerField("Window (frames)");
            windowField.tooltip =
                "How many frames the event's AnimEventMask bit stays open. 0 makes it pulse-only: "
                + "it still fires with its payload, it just holds no state.";
            windowField.SetValueWithoutNotify(Mathf.RoundToInt(marker.windowSeconds * frameRate));
            windowField.RegisterValueChangedCallback(changeEvent =>
            {
                EditEventMarker(address, "Edit Event Window", editedMarker =>
                {
                    editedMarker.windowSeconds = Mathf.Max(0, changeEvent.newValue) / frameRate;
                    return editedMarker;
                });
            });
            inspectorPane.Add(windowField);

            if (marker.windowSeconds > 0f)
            {
                inspectorPane.Add(MakeHint(
                    marker.windowSeconds.ToString("0.###") + "s at "
                    + frameRate.ToString("0.##") + " fps"));
            }
        }

        /// <summary>The entry holding a specific key, or null when the registry does not have it.</summary>
        internal static AnimEventKeyEntry FindRegistryEntryByKey(
            AnimEventKeyRegistry registry, uint eventKey)
        {
            if (registry == null || registry.entries == null)
            {
                return null;
            }
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = registry.entries[entryIndex];
                if (entry != null && entry.eventKey == eventKey)
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>The one-line status under the event button: its name, and whether it can hold a window.</summary>
        private static string DescribeEventKey(uint eventKey, AnimEventKeyRegistry registry)
        {
            string displayName = DescribeEventName(eventKey, registry);
            if (eventKey < (uint)ReservedEventKeys.FirstUserKey)
            {
                return displayName + " is reserved by the package — this clip will fail "
                    + "validation (V09).";
            }
            if (!AnimEventMaskKeys.IsMaskable(eventKey))
            {
                return displayName
                    + " · pulse-only (outside the maskable range, so a window here would never open).";
            }
            return displayName + " · mask bit " + (eventKey - AnimEventMaskKeys.FirstMaskKey) + ".";
        }

        /// <summary>The project-wide event registry; the only source now that the per-set override is gone.</summary>
        internal static AnimEventKeyRegistry ResolveEventKeyRegistry()
        {
            return VocabularyRegistryProvider.AnimEventKeys;
        }

        /// <summary>The registry's display rate, or the package default when there is no registry.</summary>
        internal static float ResolveReferenceFrameRate(AnimEventKeyRegistry registry)
        {
            if (registry == null || registry.referenceFrameRate < 1f)
            {
                return AnimEventKeyRegistry.DefaultReferenceFrameRate;
            }
            return registry.referenceFrameRate;
        }

        /// <summary>Applies one undoable edit to an event marker and refreshes what shows it.</summary>
        private void EditEventMarker(
            KeyAddress address, string undoLabel, System.Func<EventMarker, EventMarker> edit)
        {
            int flatIndex = ResolveEventFlatIndex(address);
            if (session.SelectedClip.events == null || flatIndex < 0)
            {
                return;
            }
            RecordClipEdit(undoLabel);
            session.SelectedClip.events[flatIndex] = edit(session.SelectedClip.events[flatIndex]);
            CommitClipEdit();

            // Requested: the payload fields are dragged, and a timeline rebuild per mouse move is
            // wasted work at best.
            RequestTimelineRebuild();
        }

        /// <summary>The selected flipbook key: stored value, mode, and what it resolves to.</summary>
        private void AddSelectedFlipbookKeyFields(KeyAddress address)
        {
            if (session.SelectedClip.spriteTracks == null
                || address.trackIndex >= session.SelectedClip.spriteTracks.Count)
            {
                return;
            }
            SpriteTrack track = session.SelectedClip.spriteTracks[address.trackIndex];
            if (track == null || track.keys == null || address.keyIndex >= track.keys.Count)
            {
                return;
            }

            SpriteKey key = track.keys[address.keyIndex];

            IntegerField valueField = new IntegerField("Index");
            valueField.SetValueWithoutNotify(key.sliceIndex);
            valueField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Edit Flipbook Key");
                SpriteKey editedKey = track.keys[address.keyIndex];
                editedKey.sliceIndex = changeEvent.newValue;
                track.keys[address.keyIndex] = editedKey;
                CommitClipEdit();
                RequestInspectorRebuild();
            });
            inspectorPane.Add(valueField);

            EnumField indexModeField = new EnumField("Index Mode", key.indexMode);
            indexModeField.RegisterValueChangedCallback(changeEvent =>
            {
                ToggleFlipbookKeyMode(
                    track, address.keyIndex, (SpriteIndexMode)changeEvent.newValue);
            });
            inspectorPane.Add(indexModeField);

            inspectorPane.Add(MakeFlipbookResolvedLabel(key, track.baseIndex));

            IntegerField baseIndexField = new IntegerField("Base Index");
            baseIndexField.SetValueWithoutNotify(track.baseIndex);
            baseIndexField.tooltip = "Shared by every relative key on this track.";
            baseIndexField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Base Index");
                track.baseIndex = changeEvent.newValue;
                CommitClipEdit();
                RequestInspectorRebuild();
            });
            inspectorPane.Add(baseIndexField);
        }

        // Dragging writes Bezier and refreshes the dropdown's label in place rather than rebuilding
        // the inspector: a rebuild mid-gesture would replace the element under the captured pointer.
        /// <summary>The selected key's easing: a named preset to start from, and the curve it draws.</summary>
        private void AddInterpolationControls(KeyAddress address)
        {
            if (address.trackKind != TimelineTrackKind.Transform
                && address.trackKind != TimelineTrackKind.Bone)
            {
                return;
            }

            Interpolation currentInterpolation = GetKeyInterpolation(address);
            float2 startHandle;
            float2 endHandle;
            GetKeyBezierHandles(address, out startHandle, out endHandle);

            inspectorPane.Add(MakeHeading("Easing"));

            EasingCurveEditorElement curveEditor = new EasingCurveEditorElement();
            curveEditor.SetCurveWithoutNotify(currentInterpolation, startHandle, endHandle);

            DropdownField presetField = new DropdownField(
                "Curve",
                new List<string>(EasingPresets.DisplayNames),
                EasingPresets.IndexOf(currentInterpolation, startHandle, endHandle));
            presetField.tooltip =
                "The shape the curve leaves this key with. Pick a preset, then drag the handles to "
                + "make it your own.";
            presetField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyEasingPreset(address, curveEditor, changeEvent.newValue);
            });

            curveEditor.curveEdited += (draggedStart, draggedEnd) =>
            {
                SetKeyCurve(address, Interpolation.Bezier, draggedStart, draggedEnd);
                presetField.SetValueWithoutNotify(EasingPresets.DisplayNameOf(
                    Interpolation.Bezier, draggedStart, draggedEnd));
            };

            inspectorPane.Add(presetField);
            inspectorPane.Add(curveEditor);
            inspectorPane.Add(MakeHint(
                "Drag the handles to reshape the curve — that turns any preset into a custom one. "
                + "They stay inside the unit square: outside it the curve stops being a function of "
                + "time, or overshoots further than the baked bounds allow."));
        }

        // Picking "Custom" keeps the shape already on screen and only changes what stores it: it is
        // a request to start editing, not a request to look different.
        /// <summary>Writes the chosen preset onto the key and onto the curve widget.</summary>
        private void ApplyEasingPreset(
            KeyAddress address, EasingCurveEditorElement curveEditor, string chosenDisplayName)
        {
            int chosenIndex = EasingPresets.IndexOfDisplayName(chosenDisplayName);
            if (chosenIndex < 0)
            {
                return;
            }

            if (EasingPresets.IsCustomIndex(chosenIndex))
            {
                float2 shownStartHandle;
                float2 shownEndHandle;
                curveEditor.GetHandles(out shownStartHandle, out shownEndHandle);
                SetKeyCurve(address, Interpolation.Bezier, shownStartHandle, shownEndHandle);
                curveEditor.SetCurveWithoutNotify(
                    Interpolation.Bezier, shownStartHandle, shownEndHandle);
                return;
            }

            EasingPreset preset = EasingPresets.At(chosenIndex);
            SetKeyCurve(address, preset.interpolation, preset.startHandle, preset.endHandle);
            curveEditor.SetCurveWithoutNotify(
                preset.interpolation, preset.startHandle, preset.endHandle);
        }

        private Interpolation GetKeyInterpolation(KeyAddress address)
        {
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                return session.SelectedClip.boneTracks[address.trackIndex].keys[address.keyIndex].interpolation;
            }
            return session.SelectedClip.transformTracks[address.trackIndex].keys[address.keyIndex].interpolation;
        }

        // Handles are written even for fixed modes, which never read them: they are the matching
        // cubic, so a key later switched to Bezier starts from the shape it was already playing.
        /// <summary>Writes a key's easing mode and its handles together.</summary>
        private void SetKeyCurve(
            KeyAddress address, Interpolation interpolation, float2 startHandle, float2 endHandle)
        {
            RecordClipEdit("Change Key Easing");
            EnsureUsableBezierHandles(ref startHandle, ref endHandle, interpolation);
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                BoneTrack track = session.SelectedClip.boneTracks[address.trackIndex];
                BoneKey key = track.keys[address.keyIndex];
                key.interpolation = interpolation;
                key.bezierStartHandle = startHandle;
                key.bezierEndHandle = endHandle;
                track.keys[address.keyIndex] = key;
            }
            else
            {
                TransformTrack track = session.SelectedClip.transformTracks[address.trackIndex];
                TransformKey key = track.keys[address.keyIndex];
                key.interpolation = interpolation;
                key.bezierStartHandle = startHandle;
                key.bezierEndHandle = endHandle;
                track.keys[address.keyIndex] = key;
            }
            CommitClipEdit();
        }

        // A key that never carried handles holds two zeros, which the sampler reads as linear;
        // writing the diagonal handles on the switch keeps the editor and the sampler agreeing.
        /// <summary>Gives a Bezier key with no handles the ones that describe a straight line.</summary>
        private static void EnsureUsableBezierHandles(
            ref float2 startHandle, ref float2 endHandle, Interpolation interpolation)
        {
            if (interpolation != Interpolation.Bezier)
            {
                return;
            }
            if (math.all(startHandle == float2.zero) && math.all(endHandle == float2.zero))
            {
                startHandle = EasingPresets.LinearStartHandle;
                endHandle = EasingPresets.LinearEndHandle;
            }
        }

        private void GetKeyBezierHandles(
            KeyAddress address, out float2 startHandle, out float2 endHandle)
        {
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                BoneKey key = session.SelectedClip.boneTracks[address.trackIndex].keys[address.keyIndex];
                startHandle = key.bezierStartHandle;
                endHandle = key.bezierEndHandle;
                return;
            }
            TransformKey transformKey =
                session.SelectedClip.transformTracks[address.trackIndex].keys[address.keyIndex];
            startHandle = transformKey.bezierStartHandle;
            endHandle = transformKey.bezierEndHandle;
        }

        /// <summary>The live pose of a bone at the playhead, editable in place.</summary>
        internal void AddBoneTransformFields(VisualElement parent, string boneName)
        {
            LiveTransformBinding binding = new LiveTransformBinding { boneName = boneName };
            liveTransformBindings.Add(binding);

            bool isRigEdit = IsRigEditMode();

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            bool hasKeys;
            bool isOnKey;
            if (isRigEdit)
            {
                // A bone track has no rest pose to fall back to (ApplyBoneEdit's own remark), so
                // outside Rig Edit an unkeyed bone reads as zero -- correct there, since zero
                // literally is "no offset yet". Rig Edit has no offset concept at all; it shows the
                // node's live preview pose, the same source RefreshRigEditGizmo pivots on.
                ReadRigEditBonePose(boneName, out position, out rotationDegrees, out scale);
                hasKeys = false;
                isOnKey = false;
            }
            else
            {
                // Resolved rather than passed in, and allowed to come back null: every object shows
                // a transform from the moment it is selected, and the track that stores its poses is
                // minted by the first key. Everything below reads a null track as "no keys", which is
                // exactly what an unkeyed node has.
                BoneTrack track = FindBoneTrack(boneName);
                hasKeys = ClipBoneEditing.TryEvaluate(
                    track, session.PlayheadNormalized, out position, out rotationDegrees, out scale);
                isOnKey = ClipBoneEditing.FindKeyIndexAt(track, session.PlayheadNormalized) >= 0;
            }

            binding.stateChip = MakeHint(isRigEdit
                ? "Base pose — drag the viewport gizmo to edit it. Rig Edit writes the prefab, not "
                    + "a key, so these fields are read-only here."
                : DescribeBoneState(hasKeys, isOnKey));
            parent.Add(binding.stateChip);

            VisualElement transformBlock = new VisualElement();
            transformBlock.AddToClassList(TransformBlockUssClassName);
            transformBlock.EnableInClassList(TransformOnKeyUssClassName, isOnKey);
            transformBlock.EnableInClassList(TransformInterpolatedUssClassName, hasKeys && !isOnKey);
            binding.block = transformBlock;

            Vector3Field positionField = new Vector3Field("Position");
            positionField.SetValueWithoutNotify(new Vector3(position.x, position.y, position.z));
            positionField.SetEnabled(!isRigEdit);
            positionField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEditFromFields(binding);
            });
            binding.positionField = positionField;
            transformBlock.Add(positionField);

            Vector3Field rotationField = new Vector3Field("Rotation");
            rotationField.SetValueWithoutNotify(
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            rotationField.SetEnabled(!isRigEdit);
            rotationField.tooltip =
                "Euler degrees. The authored key stores a quaternion; this is the readable form of "
                + "it, converted at the boundary.";
            rotationField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEditFromFields(binding);
            });
            binding.rotationField = rotationField;
            transformBlock.Add(rotationField);

            Vector3Field scaleField = new Vector3Field("Scale");
            scaleField.SetValueWithoutNotify(new Vector3(scale.x, scale.y, scale.z));
            scaleField.SetEnabled(!isRigEdit);
            scaleField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEditFromFields(binding);
            });
            binding.scaleField = scaleField;
            transformBlock.Add(scaleField);

            parent.Add(transformBlock);

            // Every route into keying is refused in Rig Edit (ApplyBoneEdit is a clip edit; this
            // mode writes the prefab), so a Key button that could not do anything would just be
            // another dead control on top of the read-only fields above.
            if (isRigEdit)
            {
                return;
            }

            parent.Add(new Button(() =>
            {
                ApplyBoneEdit(boneName, position, rotationDegrees, scale);
            })
            {
                text = "Key"
            });
        }

        /// <summary>
        /// A skinned bone or bare grouping transform's live preview pose, for Rig Edit's read-only
        /// display -- the same source <see cref="RefreshRigEditGizmo"/> pivots on, found by name
        /// since a component block only has the bone name.
        /// </summary>
        private void ReadRigEditBonePose(
            string boneName, out float3 position, out float3 rotationDegrees, out float3 scale)
        {
            int previewIndex = previewController != null
                ? previewController.FindHierarchyIndexByName(boneName)
                : -1;
            Transform node = previewIndex >= 0 ? previewController.GetTransformByIndex(previewIndex) : null;
            if (node == null)
            {
                position = float3.zero;
                rotationDegrees = float3.zero;
                scale = new float3(1f, 1f, 1f);
                return;
            }

            position = new float3(node.localPosition.x, node.localPosition.y, node.localPosition.z);
            Vector3 nodeEuler = node.localEulerAngles;
            rotationDegrees = new float3(nodeEuler.x, nodeEuler.y, nodeEuler.z);
            scale = new float3(node.localScale.x, node.localScale.y, node.localScale.z);
        }

        /// <summary>The bone track posing a node on this clip, or null when nothing keys it yet.</summary>
        private BoneTrack FindBoneTrack(string boneName)
        {
            if (session.SelectedClip == null || session.SelectedClip.boneTracks == null
                || string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            for (int trackIndex = 0; trackIndex < session.SelectedClip.boneTracks.Count; trackIndex++)
            {
                BoneTrack track = session.SelectedClip.boneTracks[trackIndex];
                if (track != null
                    && string.Equals(track.boneName, boneName, System.StringComparison.Ordinal))
                {
                    return track;
                }
            }
            return null;
        }

        private static string DescribeBoneState(bool hasKeys, bool isOnKey)
        {
            if (!hasKeys)
            {
                return "No keys yet — editing creates the first one.";
            }
            return isOnKey
                ? "On a key — editing changes this key."
                : "Between keys — this value is sampled, not stored.";
        }

        // Always keys, unlike a transform edit: a bone track has no rest pose in this window to
        // fall back to, so a held-but-unkeyed value would vanish on the next scrub.
        /// <summary>Writes a bone pose at the playhead, creating the track if this is its first key.</summary>
        private void ApplyBoneEdit(
            string boneName, float3 position, float3 rotationDegrees, float3 scale)
        {
            if (session.SelectedClip == null || string.IsNullOrEmpty(boneName))
            {
                return;
            }

            RecordClipEdit("Key Bone");

            BoneTrack track = FindBoneTrack(boneName);
            bool isFirstKey = track == null;
            if (isFirstKey)
            {
                if (session.SelectedClip.boneTracks == null)
                {
                    session.SelectedClip.boneTracks = new List<BoneTrack>();
                }
                track = new BoneTrack
                {
                    boneName = boneName,
                    keys = new List<BoneKey>()
                };
                session.SelectedClip.boneTracks.Add(track);
            }

            ClipBoneEditing.SetKeyValues(track, session.PlayheadNormalized, position, rotationDegrees, scale);
            CommitClipEdit();

            session.SelectedKeys.Clear();
            session.HasActiveKey = false;

            // Requested, not run: a drag calls this on every mouse move, and rebuilding the timeline
            // per move is the stutter even where it does not destroy the field outright.
            RequestTimelineRebuild();

            // Only the first key rebuilds the panels around the field. It is the one that changes
            // what they say — the row becomes animated and the component stops reading "not keyed"
            // — and a rebuild on every keystroke would destroy the field being typed into.
            if (isFirstKey)
            {
                RequestHierarchyRebuild();
                RequestInspectorRebuild();
            }
        }

        /// <summary>Writes a bone transform block's three fields as one key, then re-states the block.</summary>
        private void ApplyBoneEditFromFields(LiveTransformBinding binding)
        {
            if (binding == null
                || string.IsNullOrEmpty(binding.boneName)
                || binding.positionField == null
                || binding.rotationField == null
                || binding.scaleField == null)
            {
                return;
            }

            ApplyBoneEdit(
                binding.boneName,
                ClipEditorWindow.ToFloat3(binding.positionField.value),
                ClipEditorWindow.ToFloat3(binding.rotationField.value),
                ClipEditorWindow.ToFloat3(binding.scaleField.value));
            RefreshLiveTransformBinding(binding);
        }

        // -------------------------------------------------------------------------------------
        // Sockets.
        // -------------------------------------------------------------------------------------

        // "Follows" is fixed here: changing it would move the socket onto a different object, so
        // that is done by removing it and adding one where it belongs. Every edit records undo on
        // the rig, not the clip: a socket is rig structure every clip in the set shares.
        /// <summary>A socket's fields: what it follows, where it sits, and what to hang off it.</summary>
        internal void AddSocketFields(VisualElement parent, SocketDefinition socket)
        {
            RigAsset rig = ActiveRig;
            if (rig == null)
            {
                return;
            }

            bool resolved = previewController != null && previewController.IsSocketResolved(socket);
            parent.Add(MakeHint(resolved
                ? "Attachment point — follows this object every frame."
                : "Follows nothing: the binding below matches no part or bone, so this socket "
                    + "will sit at the actor's origin."));

            parent.Add(new Button(() => FocusSocket(socket.Id.Value))
            {
                text = "Move in View",
                tooltip =
                    "Puts the viewport gizmo on this socket's marker. W and E then move and rotate "
                    + "it, writing the offset below."
            });

            TextField nameField = new TextField("Name");
            nameField.SetValueWithoutNotify(socket.displayName);
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rename Socket");
                socket.displayName = changeEvent.newValue;
                CommitSocketEdit(false);
            });
            parent.Add(nameField);

            // The binding is stated, not offered. This socket is a component of the object it
            // follows, so rebinding it is removing it here and adding one where it belongs —
            // a dropdown that silently moved it into another object's stack would read as a
            // disappearance.
            Label followsLabel = MakeHint(socket.mode == SocketAttachMode.RigTarget
                ? "Follows this rig target, live."
                : "Follows this bone, whose motion is baked into the VAT.");
            parent.Add(followsLabel);

            if (socket.mode == SocketAttachMode.Bone)
            {
                parent.Add(MakeSocketBakeHint(socket));

                IntegerField layerField = new IntegerField("Layer");
                layerField.SetValueWithoutNotify(socket.layerIndex);
                layerField.tooltip =
                    "Which playback layer drives this socket's time. Only meaningful for a bone "
                    + "socket, whose pose comes from the baked track rather than from a live part.";
                layerField.RegisterValueChangedCallback(changeEvent =>
                {
                    RecordSocketEdit(rig, "Change Socket Layer");
                    socket.layerIndex = Mathf.Max(0, changeEvent.newValue);
                    CommitSocketPlacementEdit();
                });
                parent.Add(layerField);
            }

            parent.Add(MakeHeading("Offset"));

            Vector3Field offsetPositionField = new Vector3Field("Position");
            offsetPositionField.SetValueWithoutNotify(socket.localPosition);
            offsetPositionField.tooltip =
                "In the followed part or bone's local space, so it stays put as the rig moves.";
            offsetPositionField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Move Socket");
                socket.localPosition = changeEvent.newValue;
                CommitSocketPlacementEdit();
            });
            parent.Add(offsetPositionField);

            Vector3Field offsetRotationField = new Vector3Field("Rotation");
            offsetRotationField.SetValueWithoutNotify(socket.localEulerAngles);
            offsetRotationField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rotate Socket");
                socket.localEulerAngles = changeEvent.newValue;
                CommitSocketPlacementEdit();
            });
            parent.Add(offsetRotationField);

            parent.Add(MakeHeading("Preview Attachment"));
            parent.Add(MakeHint(
                "Editor only. Hangs a prefab off this socket so the placement can be judged "
                + "against the animation; nothing reads it at run time or ships in a build."));

            ObjectField attachmentField = new ObjectField("Prefab");
            attachmentField.objectType = typeof(GameObject);
            attachmentField.allowSceneObjects = false;
            attachmentField.SetValueWithoutNotify(socket.previewAttachment);
            attachmentField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Set Socket Preview Attachment");
                socket.previewAttachment = changeEvent.newValue as GameObject;
                CommitSocketEdit(false);

                if (previewController != null)
                {
                    previewController.RefreshSocketAttachments();
                }
                MarkPreviewDirty();
            });
            parent.Add(attachmentField);

        }

        // Only a bone socket needs baking: a rig-target socket's motion is its part's transform,
        // resolved live every frame, while a bone socket follows a bone that exists at run time
        // only as VAT texels, so its motion has to be sampled and stored ahead of time.
        /// <summary>Says whether a bone socket has baked motion yet, and for how many clips.</summary>
        private Label MakeSocketBakeHint(SocketDefinition socket)
        {
            VatTextureSetAsset textures = selection.ClipSet != null ? selection.ClipSet.vatTextures : null;
            if (textures == null)
            {
                return MakeHint(
                    "Not baked: this clip set has no VAT texture set. A bone socket's motion is "
                    + "captured by the VAT bake — until then it resolves to the actor's origin at "
                    + "run time. Window ▸ DOTS Animation Toolkit ▸ VAT Bake.");
            }

            int bakedClipCount = 0;
            for (int trackIndex = 0;
                textures.socketTracks != null && trackIndex < textures.socketTracks.Count;
                trackIndex++)
            {
                VatSocketTrack track = textures.socketTracks[trackIndex];
                if (track != null && track.socketId == socket.Id.Value)
                {
                    bakedClipCount++;
                }
            }

            if (bakedClipCount == 0)
            {
                return MakeHint(
                    "Not baked: no captured motion for this socket. Re-run the VAT bake, and check "
                    + "the Console for unresolved bone names while you are there.");
            }
            return MakeHint("Baked across " + bakedClipCount.ToString() + " clip(s).");
        }

        /// <summary>A dropdown of the rig's parts, so a target binding cannot be mistyped.</summary>
        internal VisualElement BuildSocketTargetField(RigAsset rig, SocketDefinition socket)
        {
            List<string> targetNames = new List<string>();
            List<uint> targetIds = new List<uint>();
            for (int targetIndex = 0; rig.targets != null && targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }
                targetNames.Add(string.IsNullOrEmpty(target.displayName)
                    ? "Target " + target.Id.Value.ToString()
                    : target.displayName);
                targetIds.Add(target.Id.Value);
            }

            if (targetNames.Count == 0)
            {
                return MakeHint("The rig declares no parts for a socket to follow.");
            }

            int currentIndex = Mathf.Max(0, targetIds.IndexOf(socket.targetId));
            PopupField<string> targetField =
                new PopupField<string>("Target", targetNames, currentIndex);
            targetField.RegisterValueChangedCallback(changeEvent =>
            {
                int chosen = targetNames.IndexOf(changeEvent.newValue);
                if (chosen < 0)
                {
                    return;
                }
                RecordSocketEdit(rig, "Rebind Socket");
                socket.targetId = targetIds[chosen];
                CommitSocketEdit(true);
            });
            return targetField;
        }

        /// <summary>A dropdown of the loaded prefab's transform names, falling back to typing.</summary>
        internal VisualElement BuildSocketBoneField(RigAsset rig, SocketDefinition socket)
        {
            previewController.CollectHierarchyNames(hierarchyNameCache);
            if (hierarchyNameCache.Count == 0)
            {
                TextField boneField = new TextField("Bone");
                boneField.SetValueWithoutNotify(socket.boneName);
                boneField.tooltip =
                    "Pick a rig with a Source Prefab above the hierarchy to pick from its bones instead.";
                boneField.RegisterValueChangedCallback(changeEvent =>
                {
                    RecordSocketEdit(rig, "Rebind Socket");
                    socket.boneName = changeEvent.newValue;
                    CommitSocketEdit(true);
                });
                return boneField;
            }

            List<string> boneNames = new List<string>(hierarchyNameCache);
            boneNames.Sort();
            int currentIndex = Mathf.Max(0, boneNames.IndexOf(socket.boneName));

            PopupField<string> bonePopup = new PopupField<string>("Bone", boneNames, currentIndex);
            bonePopup.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rebind Socket");
                socket.boneName = changeEvent.newValue;
                CommitSocketEdit(true);
            });
            return bonePopup;
        }

        /// <summary>A block's name, marked when it is the active row.</summary>
        internal SelectionHeadingElement MakeSelectionHeading(string name, bool isActive)
        {
            Label label = MakeHeading(isActive ? name + "   (active)" : name);
            label.AddToClassList(SelectionHeadingUssClassName);
            label.EnableInClassList(SelectionHeadingActiveUssClassName, isActive);

            // Part tag button: shown only when the row names a claimed rig target, bound in
            // BuildComponentStack once the target is resolved. Built here rather than left null so
            // the row layout (label grown, button at the far edge) is correct before the caller
            // fills it in.
            Button tagButton = new Button();
            tagButton.AddToClassList(SelectionHeadingTagButtonUssClassName);
            tagButton.tooltip = "The role this part plays, stored on the rig — shared by every "
                + "clip in this set. A clip that binds a track to this tag plays on any other rig "
                + "that tags a part the same way.";

            SelectionHeadingElement row = new SelectionHeadingElement(label, tagButton);
            row.AddToClassList(SelectionHeadingRowUssClassName);
            row.Add(label);
            row.Add(tagButton);
            return row;
        }

        /// <summary>One selection heading: the part's name, plus its rig-level tag button at the far edge.</summary>
        internal sealed class SelectionHeadingElement : VisualElement
        {
            public readonly Label label;
            public readonly Button tagButton;

            public SelectionHeadingElement(Label label, Button tagButton)
            {
                this.label = label;
                this.tagButton = tagButton;
            }
        }

        /// <summary>
        /// One track's settings plus the value it is showing at the playhead, editable in place.
        /// </summary>
        internal VisualElement BuildFlipbookTrackBlock(SpriteTrack track, int trackIndex)
        {
            VisualElement trackBlock = new VisualElement();
            trackBlock.AddToClassList(FlipbookTrackUssClassName);

            int keyCount = track.keys != null ? track.keys.Count : 0;
            int effectiveKeyIndex = ClipSpriteEditing.FindEffectiveKeyIndex(track, session.PlayheadNormalized);
            bool isOnKey = ClipSpriteEditing.FindKeyIndexAt(track, session.PlayheadNormalized) >= 0;

            trackBlock.Add(MakeHeading("Track " + trackIndex + "  ·  " + keyCount + " key(s)"));

            Label stateHint = MakeHint(keyCount == 0
                ? "Empty — editing the index below creates the first key."
                : (isOnKey
                    ? "On a key — editing changes this key."
                    : "Held from an earlier key — editing keys the value here."));
            trackBlock.Add(stateHint);

            LiveFlipbookBinding binding = new LiveFlipbookBinding
            {
                track = track,
                stateHint = stateHint
            };
            liveFlipbookBindings.Add(binding);

            if (keyCount > 0 && effectiveKeyIndex >= 0)
            {
                SpriteKey currentKey = track.keys[effectiveKeyIndex];

                IntegerField valueField = new IntegerField("Index");
                valueField.SetValueWithoutNotify(currentKey.sliceIndex);
                valueField.tooltip =
                    "The number this key stores: an array index in Absolute mode, or an offset from "
                    + "the base index in RelativeToBase.";
                valueField.RegisterValueChangedCallback(changeEvent =>
                {
                    ApplyFlipbookEdit(track, changeEvent.newValue, currentKey.indexMode);
                });
                binding.valueField = valueField;
                trackBlock.Add(valueField);

                EnumField indexModeField = new EnumField("Index Mode", currentKey.indexMode);
                indexModeField.tooltip =
                    "Absolute names a frame outright. RelativeToBase holds an offset from the "
                    + "track's base index. Switching keeps the frame the key shows.";
                indexModeField.RegisterValueChangedCallback(changeEvent =>
                {
                    ToggleFlipbookKeyMode(
                        track, effectiveKeyIndex, (SpriteIndexMode)changeEvent.newValue);
                });
                binding.indexModeField = indexModeField;
                trackBlock.Add(indexModeField);

                Label resolvedLabel = MakeFlipbookResolvedLabel(currentKey, track.baseIndex);
                binding.resolvedLabel = resolvedLabel;
                trackBlock.Add(resolvedLabel);
            }
            else
            {
                IntegerField emptyValueField = new IntegerField("Index");
                emptyValueField.SetValueWithoutNotify(0);
                emptyValueField.RegisterValueChangedCallback(changeEvent =>
                {
                    ApplyFlipbookEdit(track, changeEvent.newValue, SpriteIndexMode.Absolute);
                });
                trackBlock.Add(emptyValueField);
            }

            IntegerField baseIndexField = new IntegerField("Base Index");
            baseIndexField.SetValueWithoutNotify(track.baseIndex);
            baseIndexField.tooltip =
                "Every RelativeToBase key on this track offsets from here. Changing it retargets "
                + "the whole track onto a different span of the texture array; the keys keep their "
                + "offsets untouched.";
            baseIndexField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Base Index");
                track.baseIndex = changeEvent.newValue;
                CommitClipEdit();

                // Every relative key's resolved index just moved, and the resolved index is what the
                // line above shows. Refreshed in place first so a drag reads true as it goes; the
                // rebuild lands when the drag ends and catches the rows this cannot reach.
                RefreshLiveInspectorValues();
                RequestInspectorRebuild();
            });
            trackBlock.Add(baseIndexField);

            EnumField frameModeField = new EnumField("Frame Mode", track.mode);
            frameModeField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Frame Mode");
                track.mode = (SpriteFrameMode)changeEvent.newValue;
                CommitClipEdit();
            });
            trackBlock.Add(frameModeField);

            EnumField sliceSpaceField = new EnumField("Slice Space", track.sliceSpace);
            sliceSpaceField.tooltip =
                "Whether this track's resolved value replaces the part's frame outright, or is "
                + "added to the rest slice the character's variant chose.";
            sliceSpaceField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Slice Space");
                track.sliceSpace = (SpriteSliceSpace)changeEvent.newValue;
                CommitClipEdit();
            });
            trackBlock.Add(sliceSpaceField);

            return trackBlock;
        }

        // Unlike a transform edit this always keys, regardless of auto-key: a flipbook value is a
        // discrete frame with no in-between to hold, so a held edit would just silently disappear.
        /// <summary>Writes a flipbook index at the playhead, creating a key there when there is none.</summary>
        private void ApplyFlipbookEdit(SpriteTrack track, int storedValue, SpriteIndexMode indexMode)
        {
            if (session.SelectedClip == null || track == null)
            {
                return;
            }

            RecordClipEdit("Edit Flipbook Index");
            ClipSpriteEditing.SetKeyValue(track, session.PlayheadNormalized, storedValue, indexMode);
            CommitClipEdit();

            session.SelectedKeys.Clear();
            session.HasActiveKey = false;

            // Requested: the Index field is dragged, and a per-move timeline rebuild would take the
            // field with it the moment the first key mints a lane.
            RequestTimelineRebuild();

            // The resolved "+5 → 12" reading is the one thing on screen this changes, and it is
            // refreshable without a rebuild.
            RefreshLiveInspectorValues();
        }

        /// <summary>Shows what a key resolves to, in the "+5 → 12" form.</summary>
        private static Label MakeFlipbookResolvedLabel(SpriteKey key, int baseIndex)
        {
            Label resolvedLabel = new Label();
            resolvedLabel.AddToClassList(FlipbookResolvedUssClassName);
            ApplyFlipbookResolvedLabel(resolvedLabel, key, baseIndex);
            return resolvedLabel;
        }

        /// <summary>Writes the "+5 → 12" reading onto an existing label.</summary>
        private static void ApplyFlipbookResolvedLabel(Label resolvedLabel, SpriteKey key, int baseIndex)
        {
            int resolvedIndex = SpriteIndexResolver.Resolve(key.sliceIndex, key.indexMode, baseIndex);

            string resolvedText;
            if (key.indexMode == SpriteIndexMode.RelativeToBase)
            {
                string offsetText = key.sliceIndex >= 0
                    ? "+" + key.sliceIndex.ToString()
                    : key.sliceIndex.ToString();
                resolvedText = offsetText + " → " + resolvedIndex.ToString();
            }
            else if (key.sliceIndex == SpriteIndexResolver.NoChangeSentinel)
            {
                resolvedText = "no change";
            }
            else
            {
                resolvedText = "→ " + resolvedIndex.ToString();
            }

            resolvedLabel.text = resolvedText;
            resolvedLabel.EnableInClassList(
                FlipbookInvalidUssClassName,
                key.indexMode == SpriteIndexMode.RelativeToBase && resolvedIndex < 0);
        }

        /// <summary>Switches a key between absolute and relative without moving the frame it shows.</summary>
        private void ToggleFlipbookKeyMode(SpriteTrack track, int keyIndex, SpriteIndexMode newMode)
        {
            SpriteKey key = track.keys[keyIndex];
            if (key.indexMode == newMode)
            {
                return;
            }

            int resolvedIndex = SpriteIndexResolver.Resolve(
                key.sliceIndex, key.indexMode, track.baseIndex);

            RecordClipEdit("Change Flipbook Key Mode");
            key.indexMode = newMode;
            key.sliceIndex = SpriteIndexResolver.StoredValueFor(
                resolvedIndex, newMode, track.baseIndex);
            track.keys[keyIndex] = key;
            CommitClipEdit();

            RebuildInspector();
        }

        // Values are read back off the fields, not closed over at build time: the block no longer
        // rebuilds on every change, so a captured value would stay stale and silently undo a
        // second field's drag.
        /// <summary>Writes a part transform block's three fields as one edit, then re-states the block.</summary>
        private void ApplyTransformEditFromFields(LiveTransformBinding binding)
        {
            if (binding == null
                || binding.positionField == null
                || binding.rotationField == null
                || binding.scaleField == null)
            {
                return;
            }

            ApplyTransformEdit(
                binding.targetId,
                ClipEditorWindow.ToFloat3(binding.positionField.value),
                ClipEditorWindow.ToFloat3(binding.rotationField.value),
                ClipEditorWindow.ToFloat3(binding.scaleField.value),
                false);
            RefreshLiveTransformBinding(binding);
        }

        /// <summary>The always-visible transform block for the selected part.</summary>
        internal void AddTransformFields(VisualElement parent, uint targetId)
        {
            LiveTransformBinding binding = new LiveTransformBinding { targetId = targetId };
            liveTransformBindings.Add(binding);

            bool isRigEdit = IsRigEditMode();

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            TransformValueState valueState;
            if (isRigEdit)
            {
                // The clip has an offset-from-rest value here (zero, if the part is unkeyed) that
                // has no relationship to where the node actually sits -- see RefreshRigEditGizmo.
                // Rig Edit shows and edits the live preview pose instead.
                ReadRigEditPose(targetId, out position, out rotationDegrees, out scale);
                valueState = TransformValueState.Unkeyed;
            }
            else
            {
                valueState = ResolveDisplayedTransform(targetId, out position, out rotationDegrees, out scale);
            }

            binding.stateChip = isRigEdit
                ? MakeHint("Base pose — drag the viewport gizmo to edit it. Rig Edit writes the "
                    + "prefab, not a key, so these fields are read-only here.")
                : MakeTransformStateChip(valueState);
            parent.Add(binding.stateChip);

            VisualElement transformBlock = new VisualElement();
            binding.block = transformBlock;
            transformBlock.AddToClassList(TransformBlockUssClassName);
            transformBlock.EnableInClassList(
                TransformOnKeyUssClassName, !isRigEdit && valueState == TransformValueState.OnKey);
            transformBlock.EnableInClassList(
                TransformInterpolatedUssClassName,
                !isRigEdit && valueState == TransformValueState.Interpolated);
            transformBlock.EnableInClassList(
                TransformModifiedUssClassName, !isRigEdit && valueState == TransformValueState.Modified);

            Vector3Field positionField = new Vector3Field("Position");
            positionField.SetValueWithoutNotify(new Vector3(position.x, position.y, position.z));
            positionField.SetEnabled(!isRigEdit);
            positionField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyTransformEditFromFields(binding);
            });
            binding.positionField = positionField;
            transformBlock.Add(positionField);

            Vector3Field rotationField = new Vector3Field("Rotation");
            rotationField.SetValueWithoutNotify(
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            rotationField.SetEnabled(!isRigEdit);
            rotationField.tooltip =
                "Euler degrees in Unity's ZXY order. The bake converts to radians once. "
                + "A flat rig leaves x and y at zero.";
            rotationField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyTransformEditFromFields(binding);
            });
            binding.rotationField = rotationField;
            transformBlock.Add(rotationField);

            Vector3Field scaleField = new Vector3Field("Scale");
            scaleField.SetValueWithoutNotify(new Vector3(scale.x, scale.y, scale.z));
            scaleField.SetEnabled(!isRigEdit);
            scaleField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyTransformEditFromFields(binding);
            });
            binding.scaleField = scaleField;
            transformBlock.Add(scaleField);

            parent.Add(transformBlock);

            // Keying is refused outright in Rig Edit (CommitPendingTransformEdit), so a Key/Revert
            // row that could not do anything would just be another dead control on top of the
            // read-only fields above.
            if (isRigEdit)
            {
                return;
            }

            VisualElement keyRow = new VisualElement();
            keyRow.AddToClassList(FlipbookKeyUssClassName);
            keyRow.Add(new Button(() =>
            {
                KeyDisplayedTransform(targetId, position, rotationDegrees, scale);
                RebuildInspector();
            })
            {
                text = "Key"
            });
            if (IsTransformEditHeldFor(targetId))
            {
                keyRow.Add(new Button(() =>
                {
                    DiscardPendingTransformEdit();
                    RebuildInspector();
                })
                {
                    text = "Revert"
                });
            }
            parent.Add(keyRow);
        }

        private static string DescribeTransformState(TransformValueState valueState)
        {
            switch (valueState)
            {
                case TransformValueState.OnKey:
                    return "On a key — editing changes this key.";
                case TransformValueState.Interpolated:
                    return "Between keys — this value is sampled, not stored.";
                case TransformValueState.Modified:
                    return "Modified, not keyed — press Key to keep it.";
                default:
                    return "No transform track yet — editing creates one.";
            }
        }

        private static Label MakeTransformStateChip(TransformValueState valueState)
        {
            Label chip = new Label(DescribeTransformState(valueState));
            chip.AddToClassList(HintUssClassName);
            chip.AddToClassList(TransformStateChipUssClassName);
            chip.EnableInClassList(
                TransformModifiedUssClassName, valueState == TransformValueState.Modified);
            return chip;
        }

        private void BuildClipInspector()
        {
            if (session.SelectedClip == null || clipSerializedObject == null)
            {
                inspectorPane.Add(MakeHint(selection.ClipSet == null
                    ? "Pick a clip set above the clip list."
                    : "Select a clip to edit its properties."));

                // Sockets are rig data, so they are listed whether or not a clip is open.
                AddSocketDirectory();
                return;
            }
            clipSerializedObject.Update();

            inspectorPane.Add(MakeHeading("Clip"));
            inspectorPane.Add(MakeClipNameField());
            AddBoundField("duration");
            AddBoundField("defaultLoop");
            AddBoundField("rig");
            AddBoneTrackControls();
            AddSocketDirectory();
            inspectorPane.Bind(clipSerializedObject);
        }

        /// <summary>Clip-level bone-track summary, plus the by-name fallback for a set with no rig assigned.</summary>
        private void AddBoneTrackControls()
        {
            inspectorPane.Add(MakeHeading("Bone Tracks"));

            int boneTrackCount = session.SelectedClip.boneTracks != null ? session.SelectedClip.boneTracks.Count : 0;
            inspectorPane.Add(new Label(boneTrackCount.ToString() + " track(s)"));

            // LoadedPrefab, not just whether a rig is assigned: a rig with no sourcePrefab yet has
            // no hierarchy to pick a bone from either, and the typed fallback covers that state.
            bool hasHierarchy = LoadedPrefab != null;
            if (hasHierarchy)
            {
                inspectorPane.Add(MakeHint("Pick a bone in the Hierarchy pane to add or edit its track."));
                return;
            }

            TextField boneNameField = new TextField("Bone Name");
            boneNameField.tooltip =
                "Pick a rig with a Source Prefab above the hierarchy to pick from it "
                + "instead. Case sensitive — the bake reports a name it cannot resolve.";
            inspectorPane.Add(boneNameField);
            inspectorPane.Add(new Button(() => AddBoneTrack(boneNameField.value))
            {
                text = "Add Bone Track"
            });
        }

        private void AddBoneTrack(string boneName)
        {
            if (session.SelectedClip == null || string.IsNullOrWhiteSpace(boneName))
            {
                return;
            }

            if (session.SelectedClip.boneTracks == null)
            {
                session.SelectedClip.boneTracks = new List<BoneTrack>();
            }

            // One track per bone. Two tracks naming the same bone is a validation error, and the
            // second one would silently lose to whichever the bake applied last — better to refuse
            // it here, where the user can see why. Reported on the timeline's status line rather
            // than the viewport's, which the preview tick overwrites thirty times a second.
            if (FindBoneTrackIndex(boneName) >= 0)
            {
                ReportStatus("A bone track for '" + boneName + "' already exists.");
                return;
            }

            BeginUndoGesture("Add Bone Track");
            session.SelectedClip.boneTracks.Add(new BoneTrack
            {
                boneName = boneName,
                keys = new List<BoneKey>()
            });
            EndUndoGesture();

            EditorUtility.SetDirty(session.SelectedClip);
            RebuildTimeline();
        }

        // isDelayed is load-bearing: without it the field commits on every keystroke, and each
        // commit is a file rename on disk.
        /// <summary>The clip's asset name, editable in place.</summary>
        private TextField MakeClipNameField()
        {
            TextField nameField = new TextField("Name");
            nameField.isDelayed = true;
            nameField.SetValueWithoutNotify(session.SelectedClip.name);
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                if (!ClipAssetUtility.RenameClip(session.SelectedClip, changeEvent.newValue))
                {
                    // Refused — an illegal or duplicate name. Put the field back to the truth rather
                    // than leaving it showing a name the asset does not have.
                    nameField.SetValueWithoutNotify(session.SelectedClip != null ? session.SelectedClip.name : string.Empty);
                    return;
                }
                ClipRenamed?.Invoke();
            });
            return nameField;
        }

        internal static Label MakeHeading(string text)
        {
            Label label = new Label(text);
            label.AddToClassList(HeadingUssClassName);
            return label;
        }

        internal static Label MakeHint(string text)
        {
            Label label = new Label(text);
            label.AddToClassList(HintUssClassName);
            return label;
        }

        private void AddBoundField(string propertyPath)
        {
            SerializedProperty property = clipSerializedObject.FindProperty(propertyPath);
            if (property != null)
            {
                inspectorPane.Add(new PropertyField(property));
            }
        }

        private SerializedProperty FindKeyProperty(KeyAddress address)
        {
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                    return FindTrackKeyProperty("transformTracks", address);
                case TimelineTrackKind.Sprite:
                    return FindTrackKeyProperty("spriteTracks", address);
                case TimelineTrackKind.Bone:
                    return FindTrackKeyProperty("boneTracks", address);
                default:
                {
                    SerializedProperty events = clipSerializedObject.FindProperty("events");
                    int flatIndex = ResolveEventFlatIndex(address);
                    if (events == null || flatIndex < 0 || flatIndex >= events.arraySize)
                    {
                        return null;
                    }
                    return events.GetArrayElementAtIndex(flatIndex);
                }
            }
        }

        private SerializedProperty FindTrackKeyProperty(string tracksPath, KeyAddress address)
        {
            SerializedProperty tracks = clipSerializedObject.FindProperty(tracksPath);
            if (tracks == null || address.trackIndex >= tracks.arraySize)
            {
                return null;
            }
            SerializedProperty keys = tracks.GetArrayElementAtIndex(address.trackIndex)
                .FindPropertyRelative("keys");
            if (keys == null || address.keyIndex >= keys.arraySize)
            {
                return null;
            }
            return keys.GetArrayElementAtIndex(address.keyIndex);
        }
    }
}
