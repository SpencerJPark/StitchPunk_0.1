// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;

namespace DotsAnimationToolkit.Editor
{
    public static class ClipRowFolderResolver
    {
        // Most common FolderPath among the clips -- ties resolve to whichever folder is seen
        // first, which is stable for a given SetClips call since entries preserve input order.
        public static string ComputeHomeFolderPath(IReadOnlyList<string> clipFolderPaths)
        {
            if (clipFolderPaths == null)
            {
                return string.Empty;
            }

            Dictionary<string, int> folderCounts = new Dictionary<string, int>();
            string mostCommonFolderPath = string.Empty;
            int mostCommonFolderCount = 0;
            for (int folderPathIndex = 0; folderPathIndex < clipFolderPaths.Count; folderPathIndex++)
            {
                string folderPath = clipFolderPaths[folderPathIndex] ?? string.Empty;
                int folderCount = folderCounts.TryGetValue(folderPath, out int existingCount) ? existingCount + 1 : 1;
                folderCounts[folderPath] = folderCount;
                if (folderCount > mostCommonFolderCount)
                {
                    mostCommonFolderCount = folderCount;
                    mostCommonFolderPath = folderPath;
                }
            }

            return mostCommonFolderPath;
        }

        public static string ResolveFolderColumnText(string clipFolderPath, string homeFolderPath)
        {
            bool clipIsOutsideHomeFolder = !string.Equals(clipFolderPath, homeFolderPath, System.StringComparison.Ordinal);
            if (!clipIsOutsideHomeFolder)
            {
                return string.Empty;
            }

            // R03: the row shows only the clip's own folder name; the full chain stays in the tooltip.
            return Path.GetFileName(clipFolderPath ?? string.Empty);
        }
    }
}
