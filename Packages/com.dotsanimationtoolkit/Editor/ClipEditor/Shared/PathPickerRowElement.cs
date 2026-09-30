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
        private string currentPath = string.Empty;

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

            string resolvedBrowseTooltip = string.IsNullOrEmpty(browseTooltip) ? "Choose a folder" : browseTooltip;
            browseButton = ToolkitChrome.MakeIconSquare(RaiseBrowseRequested, "FolderOpened Icon", resolvedBrowseTooltip);
            browseButton.name = "path-browse-button";
            Add(browseButton);

            Path = string.Empty;
        }

        public string Path
        {
            get => currentPath;
            set
            {
                currentPath = value ?? string.Empty;
                bool isEmpty = currentPath.Length == 0;
                pathLabel.text = isEmpty ? "No folder chosen" : currentPath;
                pathLabel.tooltip = isEmpty ? "Press the folder button to choose one" : currentPath;
                pathLabel.EnableInClassList("toolkit-path-row__path--empty", isEmpty);
            }
        }

        public event Action BrowseRequested;

        private void RaiseBrowseRequested()
        {
            BrowseRequested?.Invoke();
        }
    }
}
