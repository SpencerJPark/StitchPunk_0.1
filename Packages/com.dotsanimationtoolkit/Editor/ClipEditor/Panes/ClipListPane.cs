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
    /// <summary>The Clips pane: the Clip Set field, the clip list and its New/Delete buttons; it selects clips through the shared session and never touches another pane.</summary>
    public sealed class ClipListPane : VisualElement, IDisposable
    {
        private const string ClipRowUssClassName = "clip-editor__clip-row";

        private ObjectField clipSetField;
        private ListView clipListView;
        private Button newClipButton;
        private Button deleteClipButton;

        private ActiveAssetSelection selection;
        private ClipEditorSession session;

        public void Bind(
            VisualElement paneRoot,
            ActiveAssetSelection sharedSelection,
            ClipEditorSession editorSession,
            ClipPreviewController preview)
        {
            selection = sharedSelection;
            session = editorSession;

            if (paneRoot != null)
            {
                clipSetField = paneRoot.Q<ObjectField>("clip-set-field");
                if (clipSetField != null)
                {
                    clipSetField.objectType = typeof(ClipSetAsset);
                    clipSetField.allowSceneObjects = false;
                    clipSetField.RegisterValueChangedCallback(OnClipSetFieldChanged);
                }

                newClipButton = paneRoot.Q<Button>("new-clip-button");
                if (newClipButton != null)
                {
                    newClipButton.clicked += CreateClip;
                    newClipButton.tooltip =
                        "Create a clip beside the clip set on disk, using the set's rig, and add it to "
                        + "the set.";
                }

                deleteClipButton = paneRoot.Q<Button>("delete-clip-button");
                if (deleteClipButton != null)
                {
                    deleteClipButton.clicked += DeleteSelectedClip;
                    deleteClipButton.tooltip =
                        "Remove the selected clip from the set, and optionally send its asset to the "
                        + "trash. Asks first.";
                }

                clipListView = paneRoot.Q<ListView>("clip-list");
                if (clipListView != null)
                {
                    clipListView.fixedItemHeight = 20f;
                    clipListView.selectionType = SelectionType.Single;
                    clipListView.makeItem = MakeClipRow;
                    clipListView.bindItem = BindClipRow;
                    clipListView.selectionChanged += OnClipSelectionChanged;
                    clipListView.itemsSource = new List<ClipAsset>();
                }

                selection.ClipSetChanged += OnClipSetChanged;
                session.SelectedClipChanged += OnSelectedClipChanged;
            }
        }

        public void Dispose()
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnClipSetChanged;
            }
            if (session != null)
            {
                session.SelectedClipChanged -= OnSelectedClipChanged;
            }
            if (clipSetField != null)
            {
                clipSetField.UnregisterValueChangedCallback(OnClipSetFieldChanged);
            }
            if (newClipButton != null)
            {
                newClipButton.clicked -= CreateClip;
            }
            if (deleteClipButton != null)
            {
                deleteClipButton.clicked -= DeleteSelectedClip;
            }
            if (clipListView != null)
            {
                clipListView.selectionChanged -= OnClipSelectionChanged;
            }
        }

        // What RestoreView did inline for the clip list before the pane existed.
        public void SelectClipRow(int clipIndex)
        {
            if (clipListView != null)
            {
                clipListView.SetSelection(clipIndex);
                clipListView.ScrollToItem(clipIndex);
            }
        }

        private void OnClipSetFieldChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            selection.SetClipSet(changeEvent.newValue as ClipSetAsset);
        }

        private void OnClipSetChanged(ClipSetAsset newClipSet)
        {
            clipSetField?.SetValueWithoutNotify(newClipSet);
            RefreshClipList();
            RefreshClipActionButtons();
        }

        private void OnSelectedClipChanged(ClipAsset clip)
        {
            RefreshClipActionButtons();
        }

        // Creates a clip in the assigned set and selects it, ready to author. Shared with the clip
        // set's own inspector via ClipAssetUtility, so a clip made here is indistinguishable from
        // one made there. Pinged as well as selected, since it is written to disk without asking where.
        private void CreateClip()
        {
            if (selection.ClipSet == null)
            {
                return;
            }

            ClipAsset newClip = ClipAssetUtility.CreateClipInSet(selection.ClipSet);
            if (newClip == null)
            {
                return;
            }

            RefreshClipList();
            EditorGUIUtility.PingObject(newClip);

            int newClipIndex = selection.ClipSet.clips != null ? selection.ClipSet.clips.IndexOf(newClip) : -1;
            if (newClipIndex >= 0)
            {
                // Through the list's own selection, so creating a clip lands in exactly the state
                // clicking one would — SelectClip, the timeline rebuild and the inspector all follow
                // from the one notification.
                clipListView.SetSelection(newClipIndex);
                clipListView.ScrollToItem(newClipIndex);
            }

            session.RequestRebuild();
        }

        // Re-points the list at the set's clips and repaints its rows.
        public void RefreshClipList()
        {
            if (clipListView == null)
            {
                return;
            }
            clipListView.itemsSource = selection.ClipSet != null && selection.ClipSet.clips != null
                ? (System.Collections.IList)selection.ClipSet.clips
                : new List<ClipAsset>();
            clipListView.Rebuild();
        }

        // Enables the Clips pane's actions for the states in which they mean something. A clip is
        // only meaningful inside a set, so "no set assigned" disables New; Delete additionally needs
        // a clip selected.
        public void RefreshClipActionButtons()
        {
            if (newClipButton != null)
            {
                newClipButton.SetEnabled(selection.ClipSet != null);
            }
            if (deleteClipButton != null)
            {
                deleteClipButton.SetEnabled(selection.ClipSet != null && session.SelectedClip != null);
            }
        }

        // Asks what to do with the selected clip, then un-registers it and optionally trashes it.
        // Three answers, since "remove from the set" and "delete the file" are different intentions
        // a two-button dialog would conflate. Deleting the asset is not undoable; removing from the set is.
        private void DeleteSelectedClip()
        {
            if (selection.ClipSet == null || session.SelectedClip == null || selection.ClipSet.clips == null)
            {
                return;
            }

            int clipIndex = selection.ClipSet.clips.IndexOf(session.SelectedClip);
            if (clipIndex < 0)
            {
                return;
            }

            ClipAsset clipToDelete = session.SelectedClip;
            int choice = EditorUtility.DisplayDialogComplex(
                "Delete Clip",
                "Delete '" + clipToDelete.name + "'?\n\n"
                + "Delete Asset sends the clip file to the trash and removes it from '"
                + selection.ClipSet.name + "'. This cannot be undone.\n\n"
                + "Remove From Set un-registers it, leaves the asset on disk, and can be undone.",
                "Delete Asset",
                "Cancel",
                "Remove From Set");

            if (choice == 1)
            {
                return;
            }

            if (choice == 0)
            {
                ClipAssetUtility.DeleteClipFromSet(selection.ClipSet, clipIndex, clipToDelete);
            }
            else
            {
                ClipAssetUtility.RemoveClipFromSet(selection.ClipSet, clipIndex);
            }

            session.SetSelectedClip(null);
            RefreshClipList();
            SelectClipNearIndex(clipIndex);

            session.RequestRebuild();
        }

        // Selects whatever now occupies removedIndex, or the last clip.
        private void SelectClipNearIndex(int removedIndex)
        {
            if (clipListView == null || selection.ClipSet == null || selection.ClipSet.clips == null
                || selection.ClipSet.clips.Count == 0)
            {
                RefreshClipActionButtons();
                return;
            }

            int nextIndex = Mathf.Clamp(removedIndex, 0, selection.ClipSet.clips.Count - 1);
            clipListView.SetSelection(nextIndex);
            clipListView.ScrollToItem(nextIndex);
        }

        private static VisualElement MakeClipRow()
        {
            Label label = new Label();
            label.AddToClassList(ClipRowUssClassName);
            return label;
        }

        private void BindClipRow(VisualElement element, int index)
        {
            Label label = element as Label;
            if (label == null || selection.ClipSet == null || selection.ClipSet.clips == null
                || index >= selection.ClipSet.clips.Count)
            {
                return;
            }
            ClipAsset clip = selection.ClipSet.clips[index];
            label.text = clip != null ? clip.name : "<missing>";
        }

        private void OnClipSelectionChanged(IEnumerable<object> selection)
        {
            ClipAsset clip = null;
            foreach (object item in selection)
            {
                clip = item as ClipAsset;
                break;
            }
            session.SetSelectedClip(clip);
        }
    }
}
