// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Editor-only helpers for a cutscene's remembered scene and its per-scene slot→GameObject
    /// bindings (Phase G spec §3, §5). Everything here is <c>UnityEditor</c> API by nature — the
    /// asset itself stores only strings, exactly so this can live outside <c>Authoring/</c>
    /// (Conformance_C) while the editor is the only thing that ever parses them.
    /// </summary>
    internal static class CutsceneSceneBindingUtility
    {
        /// <summary>The AssetDatabase GUID of the currently active scene, or empty if it has never been saved.</summary>
        public static string CurrentSceneGuid()
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                return string.Empty;
            }
            return AssetDatabase.AssetPathToGUID(activeScene.path);
        }

        /// <summary>The currently active scene's asset path, for display.</summary>
        public static string CurrentScenePath()
        {
            return EditorSceneManager.GetActiveScene().path;
        }

        /// <summary>
        /// Prompts to save any modified scenes, then opens <paramref name="scenePath"/>. Mirrors the
        /// "prompting to save" clause of spec §3.
        /// </summary>
        public static bool TryOpenScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return false;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }
            return EditorSceneManager.OpenScene(scenePath).IsValid();
        }

        /// <summary>Finds the binding entry for one slot in the current scene, or null if unbound.</summary>
        public static CutsceneSlotBindingEntry FindBinding(CutsceneAsset cutscene, string sceneGuid, uint slotId)
        {
            if (cutscene == null || cutscene.sceneBindings == null || string.IsNullOrEmpty(sceneGuid))
            {
                return null;
            }
            for (int bindingIndex = 0; bindingIndex < cutscene.sceneBindings.Count; bindingIndex++)
            {
                CutsceneSceneBinding binding = cutscene.sceneBindings[bindingIndex];
                if (binding == null || binding.sceneGuid != sceneGuid || binding.slotBindings == null)
                {
                    continue;
                }
                for (int entryIndex = 0; entryIndex < binding.slotBindings.Count; entryIndex++)
                {
                    CutsceneSlotBindingEntry entry = binding.slotBindings[entryIndex];
                    if (entry != null && entry.slotId == slotId)
                    {
                        return entry;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Resolves a bound slot's <c>GlobalObjectId</c> string back to a live GameObject in the
        /// currently open scene. Returns null when it does not parse or the object no longer exists —
        /// both ordinary states (a deleted GameObject, a stale string), never an error.
        /// </summary>
        public static GameObject ResolveGameObject(string globalObjectId)
        {
            if (string.IsNullOrEmpty(globalObjectId))
            {
                return null;
            }
            GlobalObjectId parsedId;
            if (!GlobalObjectId.TryParse(globalObjectId, out parsedId))
            {
                return null;
            }
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedId) as GameObject;
        }

        /// <summary>
        /// Writes (or clears) one slot's binding for the current scene, through
        /// <paramref name="serializedObject"/> so the edit lands on the Undo stack like every other
        /// authored change. Pass a null <paramref name="boundGameObject"/> to unbind.
        /// </summary>
        public static void SetBinding(
            SerializedObject serializedObject, string sceneGuid, uint slotId, GameObject boundGameObject)
        {
            if (serializedObject == null || string.IsNullOrEmpty(sceneGuid))
            {
                return;
            }

            SerializedProperty sceneBindingsProperty = serializedObject.FindProperty("sceneBindings");
            SerializedProperty sceneBindingProperty =
                FindOrCreateSceneBindingProperty(sceneBindingsProperty, sceneGuid);
            SerializedProperty slotBindingsProperty =
                sceneBindingProperty.FindPropertyRelative("slotBindings");

            int existingEntryIndex = -1;
            for (int entryIndex = 0; entryIndex < slotBindingsProperty.arraySize; entryIndex++)
            {
                SerializedProperty entryProperty = slotBindingsProperty.GetArrayElementAtIndex(entryIndex);
                if (entryProperty.FindPropertyRelative("slotId").uintValue == slotId)
                {
                    existingEntryIndex = entryIndex;
                    break;
                }
            }

            if (boundGameObject == null)
            {
                if (existingEntryIndex >= 0)
                {
                    slotBindingsProperty.DeleteArrayElementAtIndex(existingEntryIndex);
                }
                serializedObject.ApplyModifiedProperties();
                return;
            }

            string globalObjectId =
                GlobalObjectId.GetGlobalObjectIdSlow(boundGameObject).ToString();

            int targetIndex = existingEntryIndex;
            if (targetIndex < 0)
            {
                targetIndex = slotBindingsProperty.arraySize;
                slotBindingsProperty.InsertArrayElementAtIndex(targetIndex);
            }
            SerializedProperty targetEntryProperty = slotBindingsProperty.GetArrayElementAtIndex(targetIndex);
            targetEntryProperty.FindPropertyRelative("slotId").uintValue = slotId;
            targetEntryProperty.FindPropertyRelative("globalObjectId").stringValue = globalObjectId;

            serializedObject.ApplyModifiedProperties();
        }

        private static SerializedProperty FindOrCreateSceneBindingProperty(
            SerializedProperty sceneBindingsProperty, string sceneGuid)
        {
            for (int bindingIndex = 0; bindingIndex < sceneBindingsProperty.arraySize; bindingIndex++)
            {
                SerializedProperty bindingProperty = sceneBindingsProperty.GetArrayElementAtIndex(bindingIndex);
                if (bindingProperty.FindPropertyRelative("sceneGuid").stringValue == sceneGuid)
                {
                    return bindingProperty;
                }
            }

            int newIndex = sceneBindingsProperty.arraySize;
            sceneBindingsProperty.InsertArrayElementAtIndex(newIndex);
            SerializedProperty newBindingProperty = sceneBindingsProperty.GetArrayElementAtIndex(newIndex);
            newBindingProperty.FindPropertyRelative("sceneGuid").stringValue = sceneGuid;
            SerializedProperty newSlotBindingsProperty = newBindingProperty.FindPropertyRelative("slotBindings");
            newSlotBindingsProperty.ClearArray();
            return newBindingProperty;
        }
    }
}
