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
            // The whole element reads as one boxed component (A75 owner pass): the header block
            // (title, Ticked only, search) carries toolkit-box__header's lighter fill, the list
            // below sits on the plain toolkit-box fill, which is visibly darker.
            AddToClassList("toolkit-box");

            VisualElement headerBlock = new VisualElement();
            headerBlock.AddToClassList("toolkit-box__header");
            headerBlock.style.flexDirection = FlexDirection.Column;
            headerBlock.style.alignItems = Align.Stretch;
            // toolkit-box__header's padding is deliberately asymmetric (4px left, 2px right) so a
            // header button sits close to the box edge elsewhere -- matched here only, so the
            // search row's own left/right insets come out equal (verified live: 5.2px each side).
            headerBlock.style.paddingRight = 4f;

            VisualElement titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.justifyContent = Justify.SpaceBetween;

            Label title = new Label("Clips");
            title.AddToClassList("toolkit-box__title");
            titleRow.Add(title);

            checkedOnlyToggle = new Toggle("Ticked only");
            checkedOnlyToggle.name = "clip-picker-checked-only";
            // Toggle inherits BaseField's 120px label min-width, meant for aligning inspector-style
            // field columns -- verified live: the label's own box was 120px wide for ~55px of text,
            // leaving a big gap before the checkbox. Zero it so the label hugs its text.
            checkedOnlyToggle.labelElement.style.minWidth = 0f;
            checkedOnlyToggle.RegisterValueChangedCallback(OnCheckedOnlyChanged);
            titleRow.Add(checkedOnlyToggle);

            headerBlock.Add(titleRow);

            // Its own full-width row rather than squeezed into the title row's right edge —
            // a fixed-width search field beside a toggle read as skewed off to one side.
            VisualElement searchRow = new VisualElement();
            searchRow.style.flexDirection = FlexDirection.Row;
            searchRow.style.marginTop = 4f;

            searchField = new ToolbarSearchField();
            searchField.name = "clip-picker-search";
            // The field's own internal content imposes a min-content width Yoga honours over
            // flexGrow/min-width — an explicit percentage width is clamped to the row unconditionally
            // (matches ClipSetsPanel's catalog search field fix).
            searchField.style.width = new Length(100f, LengthUnit.Percent);
            searchField.style.minWidth = 0f;
            // ToolbarSearchField's own default USS ships a 4px-left/2px-right margin (verified
            // live on ClipSetsPanel's identical field) -- on a 100%-wide box that skews it right
            // of the row it sits in. Zero it for even spacing on both sides.
            searchField.style.marginLeft = 0f;
            searchField.style.marginRight = 0f;
            searchField.RegisterValueChangedCallback(OnSearchTextChanged);
            searchRow.Add(searchField);

            headerBlock.Add(searchRow);
            Add(headerBlock);

            countLabel = new Label();
            countLabel.name = "clip-picker-count";
            countLabel.AddToClassList("clip-editor__hint");
            countLabel.style.marginLeft = 6f;
            countLabel.style.marginTop = 4f;
            Add(countLabel);

            clipListView = new ListView();
            clipListView.name = "clip-picker-list";
            clipListView.fixedItemHeight = 22f;
            clipListView.selectionType = SelectionType.None;
            clipListView.style.flexGrow = 1f;
            clipListView.style.marginTop = 2f;
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
