// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The route from the Clip Editor into Unity's own prefab-authoring mode. Structural editing is
    /// handed over rather than reimplemented; objects are addressed by hierarchy path, not by
    /// reference, since the preview and prefab mode hold different instances in different scenes.
    /// </summary>
    public static class PrefabAuthoringBridge
    {
        /// <summary>Separates path segments. Matches Unity's own convention for transform paths.</summary>
        public const char PathSeparator = '/';

        /// <summary>Whether a prefab asset can be opened for <paramref name="prefab"/>.</summary>
        public static bool CanOpen(GameObject prefab)
        {
            return !string.IsNullOrEmpty(ResolveAssetPath(prefab));
        }

        // The asset path of the prefab `prefab` belongs to, or empty. Handles both a prefab asset
        // and an instance of one, since the rig field accepts either.
        public static string ResolveAssetPath(GameObject prefab)
        {
            if (prefab == null)
            {
                return string.Empty;
            }

            string directPath = AssetDatabase.GetAssetPath(prefab);
            if (!string.IsNullOrEmpty(directPath))
            {
                return directPath;
            }

            GameObject sourceAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
            return sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : string.Empty;
        }

        /// <summary>
        /// Opens <paramref name="prefab"/> in prefab mode, optionally selecting and framing one of
        /// its objects.
        /// </summary>
        /// <param name="prefab">The prefab asset, or an instance of it.</param>
        /// <param name="hierarchyPath">
        /// Path of the object to select, relative to the prefab root and excluding the root's own
        /// name. Empty selects the root.
        /// </param>
        /// <returns>False when there is no prefab asset to open.</returns>
        public static bool OpenPrefab(GameObject prefab, string hierarchyPath)
        {
            string assetPath = ResolveAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            PrefabStage stage = PrefabStageUtility.OpenPrefab(assetPath);
            if (stage == null || stage.prefabContentsRoot == null)
            {
                return false;
            }

            Transform target = ResolveByPath(stage.prefabContentsRoot.transform, hierarchyPath);
            Selection.activeGameObject =
                target != null ? target.gameObject : stage.prefabContentsRoot;

            // Framing is deferred a tick. The stage's scene view is opened by the call above and is
            // not ready to frame anything until it has laid out, so framing inline lands on the
            // previous view or on nothing at all.
            EditorApplication.delayCall += FrameCurrentSelection;
            return true;
        }

        private static void FrameCurrentSelection()
        {
            if (SceneView.lastActiveSceneView == null || Selection.activeGameObject == null)
            {
                return;
            }
            SceneView.lastActiveSceneView.FrameSelected();
        }

        /// <summary>Selects the asset in the Project window and flashes it.</summary>
        public static void PingInProject(GameObject prefab)
        {
            string assetPath = ResolveAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null)
            {
                return;
            }
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        // Selects the matching object in whatever is currently open — a prefab stage or the scene.
        // Deliberately does not open anything, or opening as a side effect would make two menu entries do the same thing.
        /// <returns>False when nothing matching is open, so the caller can say so.</returns>
        public static bool SelectInOpenStageOrScene(GameObject prefab, string hierarchyPath)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
            {
                Transform staged = ResolveByPath(stage.prefabContentsRoot.transform, hierarchyPath);
                if (staged != null)
                {
                    Selection.activeGameObject = staged.gameObject;
                    return true;
                }
            }

            GameObject sceneInstance = FindSceneInstance(prefab);
            if (sceneInstance == null)
            {
                return false;
            }

            Transform inScene = ResolveByPath(sceneInstance.transform, hierarchyPath);
            Selection.activeGameObject =
                inScene != null ? inScene.gameObject : sceneInstance;
            EditorGUIUtility.PingObject(Selection.activeGameObject);
            return true;
        }

        /// <summary>The first loaded-scene instance of <paramref name="prefab"/>, or null.</summary>
        private static GameObject FindSceneInstance(GameObject prefab)
        {
            string assetPath = ResolveAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            GameObject[] sceneObjects =
                Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
            for (int objectIndex = 0; objectIndex < sceneObjects.Length; objectIndex++)
            {
                GameObject candidate = sceneObjects[objectIndex];
                if (PrefabUtility.GetPrefabAssetType(candidate) == PrefabAssetType.NotAPrefab)
                {
                    continue;
                }
                if (PrefabUtility.GetNearestPrefabInstanceRoot(candidate) != candidate)
                {
                    continue;
                }
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate) == assetPath)
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// The path of <paramref name="node"/> below <paramref name="root"/>, excluding the root's
        /// own name. Empty when they are the same object.
        /// </summary>
        /// <returns>Empty also when <paramref name="node"/> is not under the root at all.</returns>
        public static string GetHierarchyPath(Transform node, Transform root)
        {
            if (node == null || root == null || node == root)
            {
                return string.Empty;
            }

            StringBuilder path = new StringBuilder();
            Transform walker = node;
            while (walker != null && walker != root)
            {
                if (path.Length > 0)
                {
                    path.Insert(0, PathSeparator);
                }
                path.Insert(0, walker.name);
                walker = walker.parent;
            }

            // Ran off the top without meeting the root: the node belongs to a different hierarchy,
            // and returning the partial path would be a path that resolves somewhere wrong.
            return walker == root ? path.ToString() : string.Empty;
        }

        // The transform at hierarchyPath below root, or null. Walks segment by segment rather than
        // using Transform.Find, so a name containing a separator cannot skip a level.
        public static Transform ResolveByPath(Transform root, string hierarchyPath)
        {
            if (root == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(hierarchyPath))
            {
                return root;
            }

            Transform walker = root;
            string[] segments = hierarchyPath.Split(PathSeparator);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                Transform next = null;
                for (int childIndex = 0; childIndex < walker.childCount; childIndex++)
                {
                    Transform child = walker.GetChild(childIndex);
                    if (child.name == segments[segmentIndex])
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null)
                {
                    return null;
                }
                walker = next;
            }
            return walker;
        }

        // The first descendant of root with this name, or null — the fallback for a bone track,
        // whose binding is a bare name rather than a path, using the same lookup the bake performs.
        public static Transform FindByName(Transform root, string nodeName)
        {
            if (root == null || string.IsNullOrEmpty(nodeName))
            {
                return null;
            }
            if (root.name == nodeName)
            {
                return root;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                if (descendants[index].name == nodeName)
                {
                    return descendants[index];
                }
            }
            return null;
        }
    }
}
