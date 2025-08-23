using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "System Bundle", menuName = "Scriptable Systems/System Bundle", order = 0)]
public class SystemBundle : ScriptableSystem
{
    [Header("Systems")]
    [SerializeField] private List<ScriptableSystem> systems = new List<ScriptableSystem>();

    public override void Initialize()
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

    public override void Tick()
    {
        foreach (var system in systems)
        {
            if (system != null)
            {
                system.Tick();
                //Debug.Log($"Initialized ScriptableSystem: {system.name}");
            }
            else
            {
                Debug.LogWarning($"{name} has a null ScriptableSystem reference in its list.");
            }
        }
    }
}

