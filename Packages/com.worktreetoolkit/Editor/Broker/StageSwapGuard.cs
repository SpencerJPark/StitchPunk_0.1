using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorktreeToolkit.Editor
{
    public sealed class StageMoveOutcome
    {
        public bool moved;
        public List<string> blockers;
        public WorktreeCliResult cliResult;
    }

    public static class StageSwapGuard
    {
        // P3: Unity silently discards unsaved in-memory edits when git changes a loaded asset/scene
        // under it, so any dirty scene or any dirty asset the move would touch must block the swap.
        public static List<string> FindBlockers(string targetReference)
        {
            List<string> blockers = new List<string>();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                blockers.Add("Play mode is on");
            }

            if (EditorApplication.isCompiling)
            {
                blockers.Add("Unity is compiling");
            }

            if (EditorApplication.isUpdating)
            {
                blockers.Add("Unity is importing assets");
            }

            for (int sceneIndex = 0; sceneIndex < EditorSceneManager.sceneCount; sceneIndex++)
            {
                UnityEngine.SceneManagement.Scene openScene = EditorSceneManager.GetSceneAt(sceneIndex);
                if (openScene.isDirty)
                {
                    string sceneLabel = string.IsNullOrEmpty(openScene.path) ? openScene.name : openScene.path;
                    blockers.Add("unsaved scene: " + sceneLabel);
                }
            }

            WorktreeCliResult changedPathsResult = WorktreeCliClient.Run("changed-paths", targetReference);
            bool parsedChangedPaths = WorktreeCliJson.TryParse(changedPathsResult, out ChangedPathsDto changedPathsDto, out string parseErrorMessage);
            if (!parsedChangedPaths)
            {
                // Fail closed: without a reliable changed-paths list we cannot prove the move is safe.
                blockers.Add("could not list changed paths: " + parseErrorMessage);
                return blockers;
            }

            HashSet<string> changedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (changedPathsDto.paths != null)
            {
                for (int pathIndex = 0; pathIndex < changedPathsDto.paths.Length; pathIndex++)
                {
                    string changedPath = changedPathsDto.paths[pathIndex];
                    if (changedPath != null)
                    {
                        changedPaths.Add(changedPath.Replace('\\', '/'));
                    }
                }
            }

            HashSet<string> reportedDirtyAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UnityEngine.Object[] allLoadedObjects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            for (int objectIndex = 0; objectIndex < allLoadedObjects.Length; objectIndex++)
            {
                UnityEngine.Object loadedObject = allLoadedObjects[objectIndex];
                if (!EditorUtility.IsPersistent(loadedObject) || !EditorUtility.IsDirty(loadedObject))
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(loadedObject);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                string normalizedAssetPath = assetPath.Replace('\\', '/');
                if (changedPaths.Contains(normalizedAssetPath) && reportedDirtyAssetPaths.Add(normalizedAssetPath))
                {
                    blockers.Add("unsaved asset that this move changes: " + normalizedAssetPath);
                }
            }

            return blockers;
        }

        public static StageMoveOutcome MoveStage(string targetReference, params string[] cliArguments)
        {
            List<string> blockers = FindBlockers(targetReference);
            if (blockers.Count > 0)
            {
                return new StageMoveOutcome { moved = false, blockers = blockers };
            }

            AssetDatabase.DisallowAutoRefresh();
            WorktreeCliResult cliResult;
            try
            {
                cliResult = WorktreeCliClient.Run(cliArguments);
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
                AssetDatabase.Refresh();
            }

            return new StageMoveOutcome { moved = cliResult.Succeeded, blockers = blockers, cliResult = cliResult };
        }
    }
}
