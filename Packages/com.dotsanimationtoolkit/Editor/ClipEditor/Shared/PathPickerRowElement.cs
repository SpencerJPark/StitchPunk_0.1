// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A shared caption + path + browse-button row, used everywhere a panel picks a folder or file path.
    /// </summary>
    public sealed class PathPickerRowElement : VisualElement
    {
        private readonly Label pathLabel;
        private readonly Button browseButton;

        public PathPickerRowElement(string captionText, string browseTooltip)
        {
            AddToClassList("toolkit-path-row");

            if (!string.IsNullOrEmpty(captionText))
            {
                Label captionLabel = new Label(captionText);
                captionLabel.AddToClassList("toolkit-path-row__caption");
                Add(captionLabel);
            }

            pathLabel = new Label();
            pathLabel.AddToClassList("toolkit-path-row__path");
            Add(pathLabel);

            browseButton = ToolkitIcons.MakeIconButton(RaiseBrowseRequested, "FolderOpened Icon", browseTooltip, "…");
            browseButton.name = "path-browse-button";
            Add(browseButton);
        }

        public string Path
        {
            get => pathLabel.text;
            set
            {
                pathLabel.text = value;
                pathLabel.tooltip = value;
            }
        }

        public event Action BrowseRequested;

        private void RaiseBrowseRequested()
        {
            BrowseRequested?.Invoke();
        }
    }
}
