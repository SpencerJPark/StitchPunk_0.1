// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's right column: the clips, cutscenes and profiles that use the selected event, each with an open button.</summary>
    public sealed class EventUsageColumn : VisualElement, IDisposable
    {
        private const double DebounceSeconds = 0.5;
        private const string MarkerDetailPrefix = "marker ";

        public event Action<UnityEngine.Object> OpenOwnerRequested;

        public uint BoundEventKey { get; private set; }

        private readonly ScrollView usageScrollView;
        private readonly Label usageCountBadge;
        private double dueTimeSinceStartup = -1.0;

        public EventUsageColumn()
        {
            name = "event-usage-column";
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;

            Add(ToolkitChrome.MakePaneHeader("Used by", out _, out VisualElement paneHeaderActionsContainer));
            usageCountBadge = ToolkitChrome.MakeBadge("0", ToolkitStatusTone.Neutral);
            paneHeaderActionsContainer.Add(usageCountBadge);

            usageScrollView = new ScrollView(ScrollViewMode.Vertical);
            usageScrollView.style.flexGrow = 1f;
            Add(usageScrollView);
        }

        public void Bind(uint eventKey)
        {
            BoundEventKey = eventKey;

            AssetReferenceIndex.Dirtied -= OnAssetReferenceIndexDirtied;
            AssetReferenceIndex.Dirtied += OnAssetReferenceIndexDirtied;

            Refresh();
        }

        public void Refresh()
        {
            usageScrollView.Clear();

            if (BoundEventKey == 0u)
            {
                usageCountBadge.style.display = DisplayStyle.None;
                usageScrollView.Add(ToolkitChrome.MakeEmptyState(
                    "event-usage-empty",
                    "No event selected",
                    "Select a key in the Keys column to list the clips and cutscenes that fire it.",
                    // No action here: this pane is driven entirely by the Keys column's selection, not something the user can act on directly.
                    null,
                    null));
                return;
            }

            List<AssetReference> references = AssetReferenceIndex.ReferencesToEventKey(BoundEventKey);

            usageCountBadge.style.display = DisplayStyle.Flex;
            usageCountBadge.text = references.Count.ToString(CultureInfo.InvariantCulture);
            usageCountBadge.tooltip = string.Format("{0} clips and cutscenes fire this event.", references.Count);

            Dictionary<UnityEngine.Object, List<string>> clipOwnerDetails = new Dictionary<UnityEngine.Object, List<string>>();
            Dictionary<UnityEngine.Object, List<string>> cutsceneOwnerDetails = new Dictionary<UnityEngine.Object, List<string>>();
            Dictionary<UnityEngine.Object, List<string>> profileOwnerDetails = new Dictionary<UnityEngine.Object, List<string>>();

            for (int referenceIndex = 0; referenceIndex < references.Count; referenceIndex++)
            {
                AssetReference reference = references[referenceIndex];
                Dictionary<UnityEngine.Object, List<string>> targetGroup;
                switch (reference.kind)
                {
                    case AssetReferenceKind.ClipEventMarker:
                        targetGroup = clipOwnerDetails;
                        break;
                    case AssetReferenceKind.CutsceneEventMarker:
                        targetGroup = cutsceneOwnerDetails;
                        break;
                    case AssetReferenceKind.ProfileRagdollEvent:
                        targetGroup = profileOwnerDetails;
                        break;
                    default:
                        continue;
                }

                if (!targetGroup.TryGetValue(reference.owner, out List<string> detailList))
                {
                    detailList = new List<string>();
                    targetGroup[reference.owner] = detailList;
                }
                if (!string.IsNullOrEmpty(reference.detail))
                {
                    detailList.Add(reference.detail);
                }
            }

            AddUsageGroup("Clips", clipOwnerDetails, FormatMarkerUsageDetailLine);
            AddUsageGroup("Cutscenes", cutsceneOwnerDetails, FormatMarkerUsageDetailLine);
            AddUsageGroup("Profiles", profileOwnerDetails, FormatProfileUsageDetailLine);
        }

        public void RequestOpenOwner(UnityEngine.Object owner)
        {
            OpenOwnerRequested?.Invoke(owner);
        }

        public void Dispose()
        {
            AssetReferenceIndex.Dirtied -= OnAssetReferenceIndexDirtied;
            EditorApplication.update -= OnEditorUpdate;
            dueTimeSinceStartup = -1.0;
        }

        private void AddUsageGroup(
            string headerText,
            Dictionary<UnityEngine.Object, List<string>> ownerDetails,
            Func<List<string>, string> formatDetailLine)
        {
            VisualElement groupBox = new VisualElement { name = "event-usage-group" };
            groupBox.AddToClassList("toolkit-box");

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-box__header");
            Label headerLabel = new Label(string.Format("{0} ({1})", headerText, ownerDetails.Count));
            headerLabel.AddToClassList("toolkit-box__title");
            headerRow.Add(headerLabel);
            groupBox.Add(headerRow);

            VisualElement bodyContainer = new VisualElement();
            bodyContainer.AddToClassList("toolkit-box__body");
            groupBox.Add(bodyContainer);

            List<UnityEngine.Object> sortedOwners = new List<UnityEngine.Object>(ownerDetails.Keys);
            sortedOwners.Sort((firstOwner, secondOwner) => string.Compare(
                firstOwner != null ? firstOwner.name : "(missing)",
                secondOwner != null ? secondOwner.name : "(missing)",
                StringComparison.Ordinal));

            for (int ownerIndex = 0; ownerIndex < sortedOwners.Count; ownerIndex++)
            {
                UnityEngine.Object owner = sortedOwners[ownerIndex];
                bodyContainer.Add(BuildUsageRow(owner, ownerDetails[owner], formatDetailLine));
            }

            usageScrollView.Add(groupBox);
        }

        private VisualElement BuildUsageRow(
            UnityEngine.Object owner, List<string> details, Func<List<string>, string> formatDetailLine)
        {
            VisualElement row = new VisualElement { name = "event-usage-row" };
            row.AddToClassList("toolkit-box__row");

            VisualElement textColumn = new VisualElement();
            textColumn.style.flexGrow = 1f;
            textColumn.style.flexShrink = 1f;
            textColumn.style.minWidth = 0f;

            string ownerName = owner != null ? owner.name : "(missing)";
            Label nameLabel = new Label(ownerName);
            textColumn.Add(nameLabel);

            Label detailLabel = new Label(formatDetailLine(details));
            detailLabel.AddToClassList("toolkit-hint");
            textColumn.Add(detailLabel);

            row.Add(textColumn);

            Button openButton = ToolkitIcons.MakeIconButton(() => RequestOpenOwner(owner), ToolkitIcons.Link, "Open", "Open");
            openButton.name = "event-usage-open-button";
            openButton.userData = owner;
            row.Add(openButton);

            row.RegisterCallback<ClickEvent>(clickEvent =>
            {
                if (clickEvent.target == openButton)
                {
                    return;
                }
                EditorGUIUtility.PingObject(owner);
            });

            return row;
        }

        private static string FormatMarkerUsageDetailLine(List<string> details)
        {
            StringBuilder lineBuilder = new StringBuilder();
            for (int detailIndex = 0; detailIndex < details.Count; detailIndex++)
            {
                string detail = details[detailIndex];
                string strippedTime = detail.StartsWith(MarkerDetailPrefix, StringComparison.Ordinal)
                    ? detail.Substring(MarkerDetailPrefix.Length)
                    : detail;

                if (detailIndex == 0)
                {
                    lineBuilder.Append(strippedTime);
                }
                else
                {
                    lineBuilder.Append(", ");
                    lineBuilder.Append(strippedTime.StartsWith("@", StringComparison.Ordinal)
                        ? strippedTime.Substring(1)
                        : strippedTime);
                }
            }
            return lineBuilder.ToString();
        }

        private static string FormatProfileUsageDetailLine(List<string> details)
        {
            return string.Join(", ", details);
        }

        private void OnAssetReferenceIndexDirtied()
        {
            bool wasAlreadyPending = dueTimeSinceStartup >= 0.0;
            dueTimeSinceStartup = EditorApplication.timeSinceStartup + DebounceSeconds;
            if (!wasAlreadyPending)
            {
                EditorApplication.update += OnEditorUpdate;
            }
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < dueTimeSinceStartup)
            {
                return;
            }
            EditorApplication.update -= OnEditorUpdate;
            dueTimeSinceStartup = -1.0;
            Refresh();
        }
    }
}
