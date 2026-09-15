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
    /// <summary>Shows a clip's tracks against a rig as a bound/skipped/dangling table, a roster
    /// coverage strip across every project rig, and a preview posing the clip on the selected rig.</summary>
    public sealed class RetargetPanel : VisualElement, IDisposable
    {
        private readonly ObjectField clipSetField;
        private readonly PopupField<ClipAsset> clipField;
        private readonly ObjectField rigField;
        private readonly RetargetTrackTableElement trackTable;
        private readonly RosterCoverageStripElement rosterStrip;
        private readonly RetargetPreviewElement preview;

        public ITransportTarget TransportTarget { get { return preview; } }

        private ActiveAssetSelection selection;
        private ClipSetAsset localClipSet;
        private ClipAsset localClip;
        private RigAsset localRig;
        private TargetTagRegistry explicitTagRegistry;
        private IReadOnlyList<RigAsset> explicitRosterRigs;
        private List<TrackBinding> resolvedBindings = new List<TrackBinding>();
        private List<RosterCoverageEntry> rosterEntries = new List<RosterCoverageEntry>();
        private bool isDisposed;

        public ClipSetAsset SelectedClipSet
        {
            get { return localClipSet; }
        }

        public ClipAsset SelectedClip
        {
            get { return localClip; }
        }

        public RigAsset SelectedRig
        {
            get { return localRig; }
        }

        public IReadOnlyList<TrackBinding> ResolvedBindings
        {
            get { return resolvedBindings; }
        }

        public IReadOnlyList<RosterCoverageEntry> RosterEntries
        {
            get { return rosterEntries; }
        }

        private TargetTagRegistry ActiveTagRegistry
        {
            get { return explicitTagRegistry != null ? explicitTagRegistry : VocabularyRegistryProvider.TargetTags; }
        }

        private IReadOnlyList<RigAsset> ActiveRosterRigs
        {
            get { return explicitRosterRigs != null ? explicitRosterRigs : AssetReferenceIndex.Rigs; }
        }

        public RetargetPanel()
        {
            name = "retarget-panel";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement headerRow = ToolkitChrome.MakeAssetBar("retarget-header-row");
            Add(headerRow);

            headerRow.Add(ToolkitChrome.MakeAssetBarLabel("Clip Set"));
            clipSetField = new ObjectField
            {
                name = "retarget-clip-set-field",
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false
            };
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
            headerRow.Add(clipSetField);

            headerRow.Add(ToolkitChrome.MakeAssetBarLabel("Clip"));
            clipField = new PopupField<ClipAsset>(string.Empty, BuildClipChoices(null), 0, FormatClipChoice, FormatClipChoice)
            {
                name = "retarget-clip-field"
            };
            clipField.AddToClassList("toolkit-asset-bar__field");
            clipField.RegisterValueChangedCallback(changeEvent => SelectClip(changeEvent.newValue));
            headerRow.Add(clipField);

            headerRow.Add(ToolkitChrome.MakeAssetBarLabel("Rig"));
            rigField = new ObjectField
            {
                name = "retarget-rig-field",
                objectType = typeof(RigAsset),
                allowSceneObjects = false
            };
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
            headerRow.Add(rigField);

            CoverPaneSplitView splitView =
                new CoverPaneSplitView("Retarget.Tracks", 0, 520f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            Add(splitView);

            trackTable = new RetargetTrackTableElement();
            trackTable.style.flexGrow = 1f;
            trackTable.RemapRequested += OnTrackRemapRequested;
            splitView.Add(trackTable);

            preview = new RetargetPreviewElement();
            preview.style.flexGrow = 1f;
            splitView.Add(preview);

            rosterStrip = new RosterCoverageStripElement();
            rosterStrip.RigChipClicked += rig =>
            {
                if (selection != null)
                {
                    selection.SetRig(rig);
                }
                else
                {
                    OnSharedRigChanged(rig);
                }
            };
            Add(rosterStrip);

            Undo.undoRedoPerformed += Refresh;
        }

        // Window path: follows the shared Clip Set and Rig; registry and roster come from the
        // package's project-wide providers, re-read on every Refresh.
        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection.RigChanged -= OnSharedRigChanged;
            }
            selection = sharedSelection;
            explicitTagRegistry = null;
            explicitRosterRigs = null;
            if (selection != null)
            {
                selection.ClipSetChanged += OnSharedClipSetChanged;
                selection.RigChanged += OnSharedRigChanged;
            }
            localClipSet = selection != null ? selection.ClipSet : null;
            localRig = selection != null ? selection.Rig : null;
            ApplyClipSetRetention(localClipSet);
            Refresh();
        }

        // Detached path: drives with everything handed in, no shared selection to react to.
        public void Bind(
            ClipSetAsset clipSet,
            ClipAsset clip,
            RigAsset rig,
            TargetTagRegistry tagRegistry,
            IReadOnlyList<RigAsset> rosterRigs)
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSharedClipSetChanged;
                selection.RigChanged -= OnSharedRigChanged;
            }
            selection = null;
            localClipSet = clipSet;
            localClip = clip;
            localRig = rig;
            explicitTagRegistry = tagRegistry;
            explicitRosterRigs = rosterRigs;
            Refresh();
        }

        public void SelectClip(ClipAsset clip)
        {
            localClip = clip;
            Refresh();
        }

        public bool RemapTrack(TrackBinding binding, uint newTagId)
        {
            bool didRemap = RetargetRemapEditing.RemapTrackTag(localClip, binding.kind, binding.trackIndex, newTagId);
            Refresh();
            return didRemap;
        }

        public void Refresh()
        {
            List<ClipAsset> clipChoices = BuildClipChoices(localClipSet);
            clipField.choices = clipChoices;

            TargetTagRegistry registry = ActiveTagRegistry;
            IReadOnlyList<RigAsset> rosterRigs = ActiveRosterRigs;

            resolvedBindings = localClip != null
                ? RetargetBindingResolver.Resolve(localClip, localRig, registry)
                : new List<TrackBinding>();
            rosterEntries = localClip != null
                ? RetargetBindingResolver.BuildRoster(localClip, rosterRigs, localRig, registry)
                : new List<RosterCoverageEntry>();

            trackTable.SetBindings(resolvedBindings);
            rosterStrip.SetRoster(rosterEntries, localRig);
            preview.Show(localClipSet, localClip, localRig);

            clipSetField.SetValueWithoutNotify(localClipSet);
            rigField.SetValueWithoutNotify(localRig);
            clipField.SetValueWithoutNotify(localClip);
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
            Undo.undoRedoPerformed -= Refresh;
            preview.Dispose();
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            localClipSet = clipSet;
            ApplyClipSetRetention(clipSet);
            Refresh();
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            localRig = rig;
            Refresh();
        }

        // Keeps the current clip if it is still in the newly selected set, else the set's first
        // clip, else none.
        private void ApplyClipSetRetention(ClipSetAsset clipSet)
        {
            ClipAsset retainedClip = null;
            if (clipSet != null && clipSet.clips != null)
            {
                if (localClip != null && clipSet.clips.Contains(localClip))
                {
                    retainedClip = localClip;
                }
                else
                {
                    for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                    {
                        if (clipSet.clips[clipIndex] != null)
                        {
                            retainedClip = clipSet.clips[clipIndex];
                            break;
                        }
                    }
                }
            }
            localClip = retainedClip;
        }

        private static List<ClipAsset> BuildClipChoices(ClipSetAsset clipSet)
        {
            List<ClipAsset> choices = new List<ClipAsset> { null };
            if (clipSet != null && clipSet.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset candidateClip = clipSet.clips[clipIndex];
                    if (candidateClip != null)
                    {
                        choices.Add(candidateClip);
                    }
                }
            }
            return choices;
        }

        private static string FormatClipChoice(ClipAsset clip)
        {
            return clip != null ? clip.name : "(none)";
        }

        private void OnTrackRemapRequested(TrackBinding binding, VisualElement anchor)
        {
            TargetTagRegistry registry = ActiveTagRegistry;
            GenericDropdownMenu menu = new GenericDropdownMenu();
            bool hasTaggedTarget = false;
            if (localRig != null && localRig.targets != null && registry != null)
            {
                for (int targetIndex = 0; targetIndex < localRig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = localRig.targets[targetIndex];
                    if (targetDefinition.tagId == 0u)
                    {
                        continue;
                    }
                    string tagName = registry.FindName(targetDefinition.tagId);
                    if (string.IsNullOrEmpty(tagName))
                    {
                        continue;
                    }
                    hasTaggedTarget = true;
                    uint remapTagId = targetDefinition.tagId;
                    bool isChecked = remapTagId == binding.tagId;
                    menu.AddItem(tagName, isChecked, () => RemapTrack(binding, remapTagId));
                }
            }
            if (!hasTaggedTarget)
            {
                menu.AddItem("This rig has no tagged parts", false, () => { });
            }
            if (binding.tagId != 0u)
            {
                menu.AddItem("Remap in every clip…", false, () =>
                {
                    RefactorPromptEditing.PickTagThenReplaceTrackTag(this, anchor, binding.tagId, Refresh);
                });
            }
            if (binding.state == TrackBindingState.Skipped && binding.CanRemapTag && binding.tagId != 0u
                && localRig != null && localRig.targets != null)
            {
                menu.AddItem("Add tag to rig part…", false, () => ShowAddTagToRigPartMenu(binding, anchor));
            }
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        // GenericDropdownMenu has no submenu support in Unity 6000.5 (a "/" in a label renders as one literal
        // item), so nesting the rig-part picker means opening a second menu on the same anchor.
        private void ShowAddTagToRigPartMenu(TrackBinding binding, VisualElement anchor)
        {
            GenericDropdownMenu menu = new GenericDropdownMenu();
            TargetTagRegistry registry = ActiveTagRegistry;
            string newTagName = registry != null ? registry.FindName(binding.tagId) : string.Empty;
            if (string.IsNullOrEmpty(newTagName))
            {
                newTagName = "this tag";
            }
            bool hasAnyTarget = localRig != null && localRig.targets != null && localRig.targets.Count > 0;
            if (hasAnyTarget)
            {
                for (int targetIndex = 0; targetIndex < localRig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = localRig.targets[targetIndex];
                    if (targetDefinition == null || targetDefinition.tagId != 0u)
                    {
                        continue;
                    }
                    string partLabel = string.IsNullOrEmpty(targetDefinition.displayName) ? "(unnamed part)" : targetDefinition.displayName;
                    uint targetStableId = targetDefinition.Id.Value;
                    menu.AddItem(partLabel, false, () => AddTagToRigPart(binding, targetStableId));
                }
                for (int targetIndex = 0; targetIndex < localRig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition targetDefinition = localRig.targets[targetIndex];
                    if (targetDefinition == null || targetDefinition.tagId == 0u)
                    {
                        continue;
                    }
                    string partLabel = string.IsNullOrEmpty(targetDefinition.displayName) ? "(unnamed part)" : targetDefinition.displayName;
                    string wornTagName = registry != null ? registry.FindName(targetDefinition.tagId) : string.Empty;
                    string menuLabel = !string.IsNullOrEmpty(wornTagName)
                        ? partLabel + " (wears " + wornTagName + ")"
                        : partLabel + " (wears another tag)";
                    string currentTagName = !string.IsNullOrEmpty(wornTagName) ? wornTagName : "another tag";
                    uint targetStableId = targetDefinition.Id.Value;
                    uint existingTagId = targetDefinition.tagId;
                    menu.AddItem(menuLabel, false, () =>
                    {
                        int clipCount = AssetReferenceIndex.ReferencesToTag(existingTagId).Count;
                        bool confirmed = EditorUtility.DisplayDialog(
                            "Replace this part's tag",
                            "\"" + partLabel + "\" on \"" + localRig.name + "\" already wears \"" + currentTagName
                                + "\", used by " + clipCount + " clip reference(s). Give it \"" + newTagName + "\" instead?",
                            "Replace", "Cancel");
                        if (confirmed)
                        {
                            AddTagToRigPart(binding, targetStableId);
                        }
                    });
                }
            }
            else
            {
                menu.AddItem("This rig has no parts", false, () => { });
            }
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        // Drive entry point: no modal dialog here so a headless drive can call it directly; the
        // confirmation dialog lives only in the menu callback above.
        public bool AddTagToRigPart(TrackBinding binding, uint targetStableId)
        {
            string failureMessage;
            bool didAdd = RetargetRemapEditing.AddTagToRigPart(localRig, targetStableId, binding.tagId, out failureMessage);
            if (!didAdd && !string.IsNullOrEmpty(failureMessage))
            {
                Debug.LogWarning(failureMessage);
            }
            Refresh();
            return didAdd;
        }
    }
}
