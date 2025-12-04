using UnityEngine;
using Rive;
using Rive.Components;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

public class RiveAnimator
{
    public RiveWidget riveWidget;
    public ViewModelInstance viewModel;

    private readonly Dictionary<string, ViewModelInstanceNumberProperty> numberProperties = new();
    private readonly Dictionary<string, ViewModelInstanceStringProperty> stringProperties = new();
    private readonly Dictionary<string, ViewModelInstanceBooleanProperty> boolProperties = new();
    private readonly Dictionary<string, ViewModelInstanceEnumProperty> enumProperties = new();
    private readonly Dictionary<string, ViewModelInstanceColorProperty> colorProperties = new();
    private readonly Dictionary<string, ViewModelInstanceTriggerProperty> triggerProperties = new();

    
    public void Initialize(RiveWidget riveWidgetInitialize)
    {
        riveWidget = riveWidgetInitialize;
        viewModel = riveWidget.StateMachine.ViewModelInstance;
    }
    


    // ================== Public Rive API Methods ==================

    public void SetNumber(string propertyName, float value)
    {
        if (TryGetOrAdd(numberProperties, propertyName, viewModel.GetNumberProperty, out var prop))
            prop.Value = value;
    }

    public void SetString(string propertyName, string value)
    {
        if (TryGetOrAdd(stringProperties, propertyName, viewModel.GetStringProperty, out var prop))
            prop.Value = value;
    }

    public void SetBool(string propertyName, bool value)
    {
        if (TryGetOrAdd(boolProperties, propertyName, viewModel.GetBooleanProperty, out var prop))
            prop.Value = value;
    }

    public void SetEnum(string propertyName, string value)
    {
        if (TryGetOrAdd(enumProperties, propertyName, viewModel.GetEnumProperty, out var prop))
            prop.Value = value;
    }

    public void SetColor(string propertyName, UnityEngine.Color value)
    {
        if (TryGetOrAdd(colorProperties, propertyName, viewModel.GetColorProperty, out var prop))
            prop.Value = value;
    }

    public void SetColor32(string propertyName, UnityEngine.Color32 value)
    {
        if (TryGetOrAdd(colorProperties, propertyName, viewModel.GetColorProperty, out var prop))
            prop.Value32 = value;
    }

    public void Trigger(string propertyName)
    {
        if (TryGetOrAdd(triggerProperties, propertyName, viewModel.GetTriggerProperty, out var prop))
            prop.Trigger();
    }

    // ================== Helper: Safe Property Caching ==================

    private bool TryGetOrAdd<T>(
        Dictionary<string, T> dict,
        string key,
        Func<string, T> fetcher,
        out T property
    ) where T : class
    {
        if (viewModel == null)
        {
            property = null;
            return false;
        }

        if (!dict.TryGetValue(key, out property))
        {
            property = fetcher(key);
            if (property != null)
                dict[key] = property;
        }

        return property != null;
    }
}
