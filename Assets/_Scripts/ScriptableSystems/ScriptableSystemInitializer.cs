using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach this to a GameObject. It holds ScriptableSystem assets
/// and calls Initialize() on each when the scene starts.
/// </summary>
public class ScriptableSystemInitializer : PersistentSingleton<ScriptableSystemInitializer>
{
    [Tooltip("List of ScriptableSystems to initialize on Start.")]
    [SerializeField] private List<ScriptableSystem> systems = new();

    private void Start()
    {
        foreach (var system in systems)
        {
            if (system != null)
            {
                system.Initialize();
                Debug.Log($"Initialized ScriptableSystem: {system.name}");
            }
            else
            {
                Debug.LogWarning($"{name} has a null ScriptableSystem reference in its list.");
            }
        }
    }
}

