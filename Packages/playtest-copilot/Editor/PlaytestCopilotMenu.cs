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

        [MenuItem(MenuRoot + "Transcribe Last Session", priority = 2)]
        private static void TranscribeLastSession()
        {
            string lastSessionFolder = EditorPrefs.GetString(LastSessionFolderPreferenceKey, string.Empty);
            if (string.IsNullOrEmpty(lastSessionFolder) || !Directory.Exists(lastSessionFolder))
            {
                EditorUtility.DisplayDialog("Playtest Copilot", "No session has been recorded yet.", "OK");
                return;
            }

            PlaytestTranscriptionRunner.TryBeginTranscription(lastSessionFolder);
        }

        [MenuItem(MenuRoot + "Transcribe Last Session", validate = true)]
        private static bool ValidateTranscribeLastSession()
        {
            return !PlaytestTranscriptionRunner.IsRunning
                && !string.IsNullOrEmpty(EditorPrefs.GetString(LastSessionFolderPreferenceKey, string.Empty));
        }

        [MenuItem(MenuRoot + "Settings", priority = 20)]
        private static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Playtest Copilot");
        }

        /// Sessions live under Assets/, so the useful thing is to select the folder in the Project
        /// window rather than open a file browser. Falls back to the OS browser only when the
        /// sessions root has been pointed somewhere the AssetDatabase cannot see.
        public static void RevealFolder(string absoluteFolderPath)
        {
            string assetPath = ToAssetPath(absoluteFolderPath);
            if (!string.IsNullOrEmpty(assetPath))
            {
                Object folderAsset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (folderAsset != null)
                {
                    Selection.activeObject = folderAsset;
                    EditorGUIUtility.PingObject(folderAsset);
                    return;
                }
            }

            string indexFile = PlaytestSessionPaths.IndexFile(absoluteFolderPath);
            EditorUtility.RevealInFinder(File.Exists(indexFile) ? indexFile : absoluteFolderPath + "/");
        }

        private static string ToAssetPath(string absolutePath)
        {
            string assetsPath = Application.dataPath.Replace('\\', '/');
            string normalised = absolutePath.Replace('\\', '/').TrimEnd('/');
            if (!normalised.StartsWith(assetsPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return "Assets" + normalised.Substring(assetsPath.Length);
        }
    }
}
