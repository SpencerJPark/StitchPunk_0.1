using System.IO;
using UnityEditor;
using UnityEngine;

namespace PlaytestCopilot.Editor
{
    /// Sessions are written outside Assets/, so they never appear in the Project window and there is
    /// nothing to click. These menu items are the only discoverable route to them.
    public static class PlaytestCopilotMenu
    {
        public const string LastSessionFolderPreferenceKey = "PlaytestCopilot.LastSessionFolder";

        private const string MenuRoot = "Tools/Playtest Copilot/";

        [MenuItem(MenuRoot + "Open Last Session Folder", priority = 0)]
        private static void OpenLastSessionFolder()
        {
            string lastSessionFolder = EditorPrefs.GetString(LastSessionFolderPreferenceKey, string.Empty);
            if (string.IsNullOrEmpty(lastSessionFolder) || !Directory.Exists(lastSessionFolder))
            {
                EditorUtility.DisplayDialog(
                    "Playtest Copilot",
                    "No session has been recorded yet on this machine.\n\nPress AI Play in the toolbar, talk, then stop play mode.",
                    "OK");
                return;
            }

            RevealFolder(lastSessionFolder);
        }

        [MenuItem(MenuRoot + "Open Last Session Folder", validate = true)]
        private static bool ValidateOpenLastSessionFolder()
        {
            return !string.IsNullOrEmpty(EditorPrefs.GetString(LastSessionFolderPreferenceKey, string.Empty));
        }

        [MenuItem(MenuRoot + "Open Sessions Folder", priority = 1)]
        private static void OpenSessionsFolder()
        {
            string sessionsRoot = PlaytestCopilotSettings.Current.ResolvedSessionsRoot;
            Directory.CreateDirectory(sessionsRoot);
            RevealFolder(sessionsRoot);
        }

        [MenuItem(MenuRoot + "Settings", priority = 20)]
        private static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Playtest Copilot");
        }

        /// RevealInFinder on a folder selects it in its parent rather than opening it, so point at a
        /// file inside when there is one — the point is to land the user among the session's files.
        public static void RevealFolder(string absoluteFolderPath)
        {
            string indexFile = PlaytestSessionPaths.IndexFile(absoluteFolderPath);
            EditorUtility.RevealInFinder(File.Exists(indexFile) ? indexFile : absoluteFolderPath + "/");
        }
    }
}
