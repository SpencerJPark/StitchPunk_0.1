#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainGeneratedLockWatcher
{
    private const string LockedFieldPropertyPath = "locked";

    private static int suppressionDepth;

    static TerrainGeneratedLockWatcher()
    {
        Undo.postprocessModifications -= LockEditedMarkedObjects;
        Undo.postprocessModifications += LockEditedMarkedObjects;
    }

    public static IDisposable SuppressLocking()
    {
        suppressionDepth++;
        return new SuppressionScope();
    }

    private sealed class SuppressionScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            suppressionDepth--;
        }
    }

    private static UndoPropertyModification[] LockEditedMarkedObjects(UndoPropertyModification[] modifications)
    {
        if (suppressionDepth > 0)
        {
            return modifications;
        }

        for (int modificationIndex = 0; modificationIndex < modifications.Length; modificationIndex++)
        {
            PropertyModification current = modifications[modificationIndex].currentValue;
            if (current == null || current.target == null)
            {
                continue;
            }

            GameObject targetGameObject = ResolveGameObject(current.target);
            if (targetGameObject == null)
            {
                continue;
            }

            LockNearestMarker(targetGameObject, current);
        }

        return modifications;
    }

    private static GameObject ResolveGameObject(UnityEngine.Object target)
    {
        Component component = target as Component;
        if (component != null)
        {
            return component.gameObject;
        }
        return target as GameObject;
    }

    private static void LockNearestMarker(GameObject targetGameObject, PropertyModification modification)
    {
        Transform walker = targetGameObject.transform;
        while (walker != null)
        {
            TerrainGeneratedMarker generatedMarker = walker.GetComponent<TerrainGeneratedMarker>();
            if (generatedMarker != null)
            {
                bool isOwnLockedField = modification.target == generatedMarker
                    && modification.propertyPath == LockedFieldPropertyPath;
                if (!isOwnLockedField && !generatedMarker.locked)
                {
                    generatedMarker.locked = true;
                    EditorUtility.SetDirty(generatedMarker);
                }
                return;
            }

            TerrainScatterMarker scatterMarker = walker.GetComponent<TerrainScatterMarker>();
            if (scatterMarker != null)
            {
                bool isOwnLockedField = modification.target == scatterMarker
                    && modification.propertyPath == LockedFieldPropertyPath;
                if (!isOwnLockedField && !scatterMarker.locked)
                {
                    scatterMarker.locked = true;
                    EditorUtility.SetDirty(scatterMarker);
                }
                return;
            }

            walker = walker.parent;
        }
    }
}
#endif
