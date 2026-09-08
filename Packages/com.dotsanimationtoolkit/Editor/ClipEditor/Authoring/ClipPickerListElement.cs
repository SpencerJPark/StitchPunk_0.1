// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Searchable, check-boxed list of clip assets — one tick per row, backed by a <see cref="ClipPickerModel"/>.</summary>
    public sealed class ClipPickerListElement : VisualElement
    {
        private readonly ClipPickerModel model = new ClipPickerModel();
        private readonly ToolbarSearchField searchField;
        private readonly Toggle checkedOnlyToggle;
        private readonly Label countLabel;
        private readonly ListView clipListView;

        public event Action<ClipAsset, bool> ClipCheckedChanged;

        public IEnumerable<ClipAsset> CheckedClips => model.CheckedClips;

        public ClipPickerListElement()
        {
            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            Label title = new Label("Clips");
            title.AddToClassList("toolkit-pane-title");
            header.Add(title);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");

            searchField = new ToolbarSearchField();
            searchField.name = "clip-picker-search";
            searchField.style.width = 180f;
            searchField.RegisterValueChangedCallback(OnSearchTextChanged);
            actions.Add(searchField);

            checkedOnlyToggle = new Toggle("Ticked only");
            checkedOnlyToggle.name = "clip-picker-checked-only";
            checkedOnlyToggle.RegisterValueChangedCallback(OnCheckedOnlyChanged);
            actions.Add(checkedOnlyToggle);

            header.Add(actions);
            Add(header);

            countLabel = new Label();
            countLabel.name = "clip-picker-count";
            countLabel.AddToClassList("clip-editor__hint");
            Add(countLabel);

            clipListView = new ListView();
            clipListView.name = "clip-picker-list";
            clipListView.fixedItemHeight = 22f;
            clipListView.selectionType = SelectionType.None;
            clipListView.style.flexGrow = 1f;
            clipListView.makeItem = MakeClipPickerRow;
            clipListView.bindItem = BindClipPickerRow;
            clipListView.itemsSource = model.VisibleEntries as IList;
            Add(clipListView);

            RefreshCountLabel();
        }

        public void SetClips(IReadOnlyList<ClipAsset> clips)
        {
            List<ClipPickerEntry> entries = new List<ClipPickerEntry>();
            if (clips != null)
            {
                for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
                {
                    ClipAsset clip = clips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }

                    string assetPath = AssetDatabase.GetAssetPath(clip);
                    string folderPath = string.IsNullOrEmpty(assetPath) ? string.Empty : Path.GetDirectoryName(assetPath).Replace('\\', '/');
                    entries.Add(new ClipPickerEntry { Clip = clip, Name = clip.name, FolderPath = folderPath ?? string.Empty });
                }
            }

            model.SetEntries(entries);
            RefreshList();
        }

        public void SetCheckedClips(IEnumerable<ClipAsset> clips)
        {
            model.SetChecked(clips);
            RefreshList();
        }

        private void OnSearchTextChanged(ChangeEvent<string> changeEvent)
        {
            model.SearchText = changeEvent.newValue;
            RefreshList();
        }

        private void OnCheckedOnlyChanged(ChangeEvent<bool> changeEvent)
        {
            model.ShowCheckedOnly = changeEvent.newValue;
            RefreshList();
        }

        private void RefreshList()
        {
            clipListView.itemsSource = model.VisibleEntries as IList;
            clipListView.Rebuild();
            RefreshCountLabel();
        }

        private void RefreshCountLabel()
        {
            countLabel.text = model.DescribeCounts();
        }

        private static VisualElement MakeClipPickerRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            Toggle tickToggle = new Toggle();
            tickToggle.name = "clip-picker-row-toggle";
            tickToggle.AddToClassList("toolkit-pane-action");
            row.Add(tickToggle);

            Label nameLabel = new Label();
            nameLabel.name = "clip-picker-row-name";
            nameLabel.AddToClassList("toolkit-box__title");
            row.Add(nameLabel);

            Label folderLabel = new Label();
            folderLabel.name = "clip-picker-row-folder";
            folderLabel.AddToClassList("clip-editor__hint");
            row.Add(folderLabel);

            tickToggle.RegisterValueChangedCallback(changeEvent =>
            {
                RowChangeContext context = (RowChangeContext)row.userData;
                if (context == null)
                {
                    return;
                }

                int currentIndex = context.Index;
                ClipPickerListElement owner = context.Owner;
                IReadOnlyList<ClipPickerEntry> visibleEntries = owner.model.VisibleEntries;
                if (currentIndex < 0 || currentIndex >= visibleEntries.Count)
                {
                    return;
                }

                ClipAsset currentClip = visibleEntries[currentIndex].Clip;
                owner.model.SetCheckedState(currentClip, changeEvent.newValue);
                owner.RefreshList();
                owner.ClipCheckedChanged?.Invoke(currentClip, changeEvent.newValue);
            });

            return row;
        }

        private void BindClipPickerRow(VisualElement element, int index)
        {
            IReadOnlyList<ClipPickerEntry> visibleEntries = model.VisibleEntries;
            if (index < 0 || index >= visibleEntries.Count)
            {
                return;
            }

            ClipPickerEntry entry = visibleEntries[index];
            element.userData = new RowChangeContext { Owner = this, Index = index };

            Toggle tickToggle = element.Q<Toggle>("clip-picker-row-toggle");
            tickToggle.SetValueWithoutNotify(model.IsChecked(entry.Clip));

            Label nameLabel = element.Q<Label>("clip-picker-row-name");
            nameLabel.text = entry.Name;

            Label folderLabel = element.Q<Label>("clip-picker-row-folder");
            folderLabel.text = entry.FolderPath;
        }

        /// <summary>Per-row state stashed in a recycled row's userData so the toggle callback reads the live index, not a captured one.</summary>
        private sealed class RowChangeContext
        {
            public ClipPickerListElement Owner;
            public int Index;
        }
    }
}
