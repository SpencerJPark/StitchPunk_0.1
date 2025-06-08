using System;
using UnityEngine;
using Rive;
using Rive.Components;

public class NPC_ActionController : MonoBehaviour
{
    [SerializeField] private RiveWidget riveWidget;
    private ViewModelInstanceStringProperty actionProperty;

    void OnEnable()
    {
        riveWidget.OnWidgetStatusChanged += () =>
        {
            if (riveWidget.Status == WidgetStatus.Loaded)
            {
                var viewModel = riveWidget.StateMachine.ViewModelInstance;
                actionProperty = viewModel.GetStringProperty("action");
            }
        };

        // Subscribe to action events
        NPC_ActionEvents.OnActionChange += HandleActionChange;
    }

    void HandleActionChange(GameObject npc, string action)
    {
        if (npc == gameObject && actionProperty != null)
        {
            actionProperty.Value = action;
        }
    }

    void OnDestroy()
    {
        NPC_ActionEvents.OnActionChange -= HandleActionChange;
    }
}