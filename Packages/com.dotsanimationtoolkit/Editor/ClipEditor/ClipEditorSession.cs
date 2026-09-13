// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The clip, key and playhead one Clip Editor window is on, shared by its panes; every setter raises its event on every call, not only on a change, because a re-select is how the window resets the playhead and rebuilds.</summary>
    public sealed class ClipEditorSession
    {
        public ClipAsset SelectedClip { get; private set; }
        // The key selection is one set with two writers, the timeline and the inspector, so the
        // set itself lives here; the active key is the one most recently clicked.
        public HashSet<KeyAddress> SelectedKeys { get; } = new HashSet<KeyAddress>();
        public KeyAddress ActiveKey { get; set; }
        public bool HasActiveKey { get; set; }
        public float PlayheadNormalized { get; private set; }

        public event Action<ClipAsset> SelectedClipChanged;
        public event Action<float> PlayheadChanged;

        // "Something structural changed; panes re-query." Raised by a pane after it created,
        // deleted or renamed a clip; the window answers with the preview and the badge.
        public event Action RebuildRequested;

        // The hierarchy pane's live selection, published every time it is applied. The list is the
        // pane's own; readers see it as it is now, not as it was when the event fired.
        internal IReadOnlyList<HierarchyItem> SelectedHierarchyItems { get; private set; } = new List<HierarchyItem>();
        internal HierarchyItem ActiveHierarchyItem { get; private set; }
        public event Action HierarchySelectionChanged;

        public void SetSelectedClip(ClipAsset clip)
        {
            SelectedClip = clip;
            SelectedClipChanged?.Invoke(clip);
        }

        public void SetPlayhead(float normalizedTime)
        {
            PlayheadNormalized = normalizedTime;
            PlayheadChanged?.Invoke(normalizedTime);
        }

        internal void SetHierarchySelection(IReadOnlyList<HierarchyItem> selectedItems, HierarchyItem activeItem)
        {
            SelectedHierarchyItems = selectedItems;
            ActiveHierarchyItem = activeItem;
            HierarchySelectionChanged?.Invoke();
        }

        public void RequestRebuild()
        {
            RebuildRequested?.Invoke();
        }
    }
}
