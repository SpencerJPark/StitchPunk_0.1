// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One row of a clip picker list: the clip asset plus the display name and folder used for search.</summary>
    public struct ClipPickerEntry
    {
        public ClipAsset Clip;
        public string Name;
        public string FolderPath;
    }

    /// <summary>Pure filtering and tick-state model behind a clip picker list — no UnityEditor or AssetDatabase calls.</summary>
    public sealed class ClipPickerModel
    {
        private readonly List<ClipPickerEntry> allEntries = new List<ClipPickerEntry>();
        private readonly List<ClipPickerEntry> visibleEntries = new List<ClipPickerEntry>();
        private readonly HashSet<ClipAsset> checkedClips = new HashSet<ClipAsset>();

        private string searchText = string.Empty;
        private bool showCheckedOnly;

        public IReadOnlyList<ClipPickerEntry> AllEntries => allEntries;

        public IReadOnlyList<ClipPickerEntry> VisibleEntries => visibleEntries;

        public string SearchText
        {
            get => searchText;
            set
            {
                searchText = value ?? string.Empty;
                ApplyFilter();
            }
        }

        public bool ShowCheckedOnly
        {
            get => showCheckedOnly;
            set
            {
                showCheckedOnly = value;
                ApplyFilter();
            }
        }

        public int CheckedCount => checkedClips.Count;

        public IEnumerable<ClipAsset> CheckedClips
        {
            get
            {
                foreach (ClipPickerEntry entry in allEntries)
                {
                    if (checkedClips.Contains(entry.Clip))
                    {
                        yield return entry.Clip;
                    }
                }
            }
        }

        public void SetEntries(IReadOnlyList<ClipPickerEntry> entries)
        {
            allEntries.Clear();
            HashSet<ClipAsset> stillPresent = new HashSet<ClipAsset>();
            if (entries != null)
            {
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    allEntries.Add(entries[entryIndex]);
                    stillPresent.Add(entries[entryIndex].Clip);
                }
            }

            checkedClips.IntersectWith(stillPresent);
            ApplyFilter();
        }

        public void SetChecked(IEnumerable<ClipAsset> clips)
        {
            checkedClips.Clear();
            if (clips != null)
            {
                foreach (ClipAsset clip in clips)
                {
                    if (clip != null)
                    {
                        checkedClips.Add(clip);
                    }
                }
            }

            ApplyFilter();
        }

        public bool IsChecked(ClipAsset clip)
        {
            return clip != null && checkedClips.Contains(clip);
        }

        public void SetCheckedState(ClipAsset clip, bool isChecked)
        {
            if (clip == null)
            {
                return;
            }

            if (isChecked)
            {
                checkedClips.Add(clip);
            }
            else
            {
                checkedClips.Remove(clip);
            }

            ApplyFilter();
        }

        public string DescribeCounts()
        {
            return $"{CheckedCount} ticked · {visibleEntries.Count} of {allEntries.Count} shown";
        }

        private void ApplyFilter()
        {
            visibleEntries.Clear();
            for (int entryIndex = 0; entryIndex < allEntries.Count; entryIndex++)
            {
                ClipPickerEntry entry = allEntries[entryIndex];
                bool matchesSearch = string.IsNullOrEmpty(searchText)
                    || (entry.Name != null && entry.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (entry.FolderPath != null && entry.FolderPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                bool matchesChecked = !showCheckedOnly || IsChecked(entry.Clip);

                if (matchesSearch && matchesChecked)
                {
                    visibleEntries.Add(entry);
                }
            }
        }
    }
}
