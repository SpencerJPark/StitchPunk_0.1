using System.Collections.Generic;
using UnityEngine;

public static class GameObjectUtils
{
    /// <summary>
    /// Enables or disables every GameObject in the list.
    /// </summary>
    /// <param name="shouldBeActive">True to activate, false to deactivate.</param>
    /// <param name="objects">The list of GameObjects to toggle.</param>
    public static void SetActiveForAll(bool shouldBeActive, List<GameObject> objects)
    {
        if (objects == null) return;
        foreach (var go in objects)
        {
            if (go != null)
                go.SetActive(shouldBeActive);
        }
    }
}
