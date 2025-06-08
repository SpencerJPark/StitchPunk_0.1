using UnityEngine;
using Rive;
using Rive.Components;
using System.Collections.Generic;

[DefaultExecutionOrder(100)]
public class RiveEnumDebugger : MonoBehaviour
{
    [SerializeField] private RiveWidget riveWidget;
    [SerializeField] private bool autoPrintOnStart = true;

    // Add all expected enum property names here
    [SerializeField] private List<string> enumPropertyNames = new();

    private ViewModelInstance viewModel;

    private void OnEnable()
    {
        if (riveWidget != null)
            riveWidget.OnWidgetStatusChanged += OnRiveReady;
    }

    private void OnDisable()
    {
        if (riveWidget != null)
            riveWidget.OnWidgetStatusChanged -= OnRiveReady;
    }

    private void OnRiveReady()
    {
        if (riveWidget.Status != WidgetStatus.Loaded) return;

        viewModel = riveWidget.StateMachine.ViewModelInstance;

        if (autoPrintOnStart)
            PrintAllEnumProperties();
    }

    public void PrintAllEnumProperties()
    {
        Debug.Log($"--- ENUM DEBUG for {name} ---");

        foreach (string key in enumPropertyNames)
        {
            var prop = viewModel.GetEnumProperty(key);
            if (prop == null)
            {
                Debug.LogWarning($"   Could not fetch property: {key}");
                continue;
            }

            Debug.Log($"[Enum] \"{key}\"");
            Debug.Log($"   Current Value: {prop.Value}");
            Debug.Log($"   Allowed Values: {string.Join(", ", prop.EnumValues)}");
        }

        Debug.Log($"--- END ENUM DEBUG ---");
    }
}
