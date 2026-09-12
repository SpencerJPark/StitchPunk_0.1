// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>A TwoPaneSplitView whose divider is remembered in EditorPrefs and re-applied after
    /// the cover pane it lives in is hidden and shown again, which otherwise floors it at minWidth.</summary>
    public sealed class CoverPaneSplitView : VisualElement
    {
        public const string PrefsKeyPrefix = "DotsAnimationToolkit.Split.";
        private const string DragLineAnchorUssClassName = "unity-two-pane-split-view__dragline-anchor";

        private readonly TwoPaneSplitView splitView;
        private readonly TwoPaneSplitViewOrientation orientation;
        private readonly int fixedPaneIndex;
        private readonly float defaultDimension;
        private readonly string prefsKey;

        private bool isReapplyPending;
        private bool isDragLineAnchorHooked;

        public CoverPaneSplitView(string prefsKeySuffix, int fixedPaneIndex, float defaultDimension, TwoPaneSplitViewOrientation orientation)
        {
            this.fixedPaneIndex = fixedPaneIndex;
            this.defaultDimension = defaultDimension;
            this.orientation = orientation;
            prefsKey = PrefsKeyPrefix + prefsKeySuffix;

            splitView = new TwoPaneSplitView(fixedPaneIndex, StoredDimension, orientation);

            style.flexGrow = 1;
            splitView.style.flexGrow = 1;
            hierarchy.Add(splitView);

            splitView.RegisterCallback<GeometryChangedEvent>(OnSplitGeometryChanged);
            RegisterCallback<AttachToPanelEvent>(evt => isReapplyPending = true);
        }

        public override VisualElement contentContainer => splitView;

        public TwoPaneSplitView SplitView => splitView;
        public string PrefsKey => prefsKey;

        public VisualElement FixedPane => childCount > fixedPaneIndex ? this[fixedPaneIndex] : null;
        public VisualElement FlexPane => childCount > 1 ? this[fixedPaneIndex == 0 ? 1 : 0] : null;

        public float StoredDimension => EditorPrefs.GetFloat(prefsKey, defaultDimension);

        public void StoreDimension(float dimension)
        {
            EditorPrefs.SetFloat(prefsKey, dimension);
        }

        public void ReapplyStoredDimension()
        {
            float stored = StoredDimension;
            splitView.fixedPaneInitialDimension = stored;

            VisualElement fixedPane = FixedPane;
            if (fixedPane == null)
            {
                return;
            }

            if (orientation == TwoPaneSplitViewOrientation.Horizontal)
            {
                fixedPane.style.width = stored;
            }
            else
            {
                fixedPane.style.height = stored;
            }
        }

        private void OnSplitGeometryChanged(GeometryChangedEvent evt)
        {
            float dimension = orientation == TwoPaneSplitViewOrientation.Horizontal ? evt.newRect.width : evt.newRect.height;
            if (dimension <= 0f)
            {
                // The zero-rect pass is the hide, and the split's own handler has just floored the
                // pane, so the next real-size pass restores it.
                isReapplyPending = true;
                return;
            }

            if (!isDragLineAnchorHooked)
            {
                VisualElement anchor = FindOwnDragLineAnchor();
                if (anchor != null)
                {
                    anchor.RegisterCallback<PointerUpEvent>(OnDragLineReleased);
                    isDragLineAnchorHooked = true;
                }
            }

            if (isReapplyPending)
            {
                isReapplyPending = false;
                ReapplyStoredDimension();
                // The split registers its own size handler in its first-layout setup, after this
                // callback, so it runs next and parks the anchor at the floor it has just measured;
                // nothing re-syncs it once the pane lays out at the stored width. Re-assigning an
                // unchanged dimension is a no-op, so the anchor is placed from the laid-out pane.
                splitView.schedule.Execute(SyncDragLineAnchorToFixedPane);
            }
        }

        // A descendant query would find a nested split's anchor first; the split's own anchor is a
        // direct hierarchy child beside its content container.
        private VisualElement FindOwnDragLineAnchor()
        {
            int directChildCount = splitView.hierarchy.childCount;
            for (int childIndex = 0; childIndex < directChildCount; childIndex++)
            {
                VisualElement child = splitView.hierarchy[childIndex];
                if (child.ClassListContains(DragLineAnchorUssClassName))
                {
                    return child;
                }
            }

            return null;
        }

        private void SyncDragLineAnchorToFixedPane()
        {
            VisualElement anchor = FindOwnDragLineAnchor();
            VisualElement fixedPane = FixedPane;
            if (anchor == null || fixedPane == null)
            {
                return;
            }

            if (orientation == TwoPaneSplitViewOrientation.Horizontal)
            {
                float paneWidth = fixedPane.resolvedStyle.width;
                anchor.style.left = fixedPaneIndex == 0 ? paneWidth : splitView.resolvedStyle.width - paneWidth;
            }
            else
            {
                float paneHeight = fixedPane.resolvedStyle.height;
                anchor.style.top = fixedPaneIndex == 0 ? paneHeight : splitView.resolvedStyle.height - paneHeight;
            }
        }

        private void OnDragLineReleased(PointerUpEvent evt)
        {
            VisualElement fixedPane = FixedPane;
            if (fixedPane == null)
            {
                return;
            }

            float dimension = orientation == TwoPaneSplitViewOrientation.Horizontal ? fixedPane.resolvedStyle.width : fixedPane.resolvedStyle.height;
            if (dimension > 0f)
            {
                StoreDimension(dimension);
            }
        }
    }
}
