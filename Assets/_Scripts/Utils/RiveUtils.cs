using UnityEngine;
using Rive;
using Rive.Components;
using System;
using System.Collections.Generic;

/// <summary>
/// Static helpers for common Rive Widget operations.
/// </summary>
public static class RiveUtils
{
    /// <summary>
    /// Subscribes to a widget's status changes and invokes <paramref name="onLoaded"/>
    /// when the widget reaches <see cref="WidgetStatus.Loaded"/>.
    /// If the widget is already loaded the callback fires immediately.
    /// </summary>
    public static void OnLoaded(RiveWidget widget, Action onLoaded)
    {
        if (widget == null || onLoaded == null) return;

        if (widget.Status == WidgetStatus.Loaded)
        {
            onLoaded();
            return;
        }

        void HandleStatusChanged()
        {
            if (widget.Status != WidgetStatus.Loaded) return;
            widget.OnWidgetStatusChanged -= HandleStatusChanged;
            onLoaded();
        }

        widget.OnWidgetStatusChanged += HandleStatusChanged;
    }

    /// <summary>
    /// Creates a <see cref="RivePropertyCache"/> backed by the widget's current
    /// <see cref="ViewModelInstance"/>, or returns null if the widget is not loaded
    /// or has no bound view model.
    /// </summary>
    public static RivePropertyCache GetPropertyCache(RiveWidget widget)
    {
        if (widget == null || widget.Status != WidgetStatus.Loaded) return null;
        ViewModelInstance vmi = widget.StateMachine?.ViewModelInstance;
        if (vmi == null) return null;
        return new RivePropertyCache(vmi);
    }
}

/// <summary>
/// Wraps a <see cref="ViewModelInstance"/> and caches property handles for
/// efficient repeated get/set access. Supports nested property paths using
/// forward-slash notation (e.g. "Parent/Child/count"), which is resolved
/// natively by the Rive runtime.
/// </summary>
public class RivePropertyCache
{
    private readonly ViewModelInstance m_viewModel;

    private readonly Dictionary<string, ViewModelInstanceNumberProperty>  m_numbers  = new Dictionary<string, ViewModelInstanceNumberProperty>();
    private readonly Dictionary<string, ViewModelInstanceStringProperty>  m_strings  = new Dictionary<string, ViewModelInstanceStringProperty>();
    private readonly Dictionary<string, ViewModelInstanceBooleanProperty> m_bools    = new Dictionary<string, ViewModelInstanceBooleanProperty>();
    private readonly Dictionary<string, ViewModelInstanceEnumProperty>    m_enums    = new Dictionary<string, ViewModelInstanceEnumProperty>();
    private readonly Dictionary<string, ViewModelInstanceColorProperty>   m_colors   = new Dictionary<string, ViewModelInstanceColorProperty>();
    private readonly Dictionary<string, ViewModelInstanceTriggerProperty> m_triggers = new Dictionary<string, ViewModelInstanceTriggerProperty>();

    public ViewModelInstance ViewModelInstance => m_viewModel;

    public RivePropertyCache(ViewModelInstance viewModelInstance)
    {
        m_viewModel = viewModelInstance;
    }

    // ── Setters ──────────────────────────────────────────────────────────────

    public void SetNumber(string path, float value)
    {
        if (TryGet(m_numbers, path, m_viewModel.GetNumberProperty, out ViewModelInstanceNumberProperty prop))
            prop.Value = value;
    }

    public void SetString(string path, string value)
    {
        if (TryGet(m_strings, path, m_viewModel.GetStringProperty, out ViewModelInstanceStringProperty prop))
            prop.Value = value;
    }

    public void SetBool(string path, bool value)
    {
        if (TryGet(m_bools, path, m_viewModel.GetBooleanProperty, out ViewModelInstanceBooleanProperty prop))
            prop.Value = value;
    }

    public void SetEnum(string path, string value)
    {
        if (TryGet(m_enums, path, m_viewModel.GetEnumProperty, out ViewModelInstanceEnumProperty prop))
            prop.Value = value;
    }

    public void SetColor(string path, UnityEngine.Color value)
    {
        if (TryGet(m_colors, path, m_viewModel.GetColorProperty, out ViewModelInstanceColorProperty prop))
            prop.Value = value;
    }

    public void SetColor32(string path, Color32 value)
    {
        if (TryGet(m_colors, path, m_viewModel.GetColorProperty, out ViewModelInstanceColorProperty prop))
            prop.Value32 = value;
    }

    public void Trigger(string path)
    {
        if (TryGet(m_triggers, path, m_viewModel.GetTriggerProperty, out ViewModelInstanceTriggerProperty prop))
            prop.Trigger();
    }

    // ── Getters ──────────────────────────────────────────────────────────────

    public float GetNumber(string path, float fallback = 0f)
    {
        return TryGet(m_numbers, path, m_viewModel.GetNumberProperty, out ViewModelInstanceNumberProperty prop)
            ? prop.Value
            : fallback;
    }

    public string GetString(string path, string fallback = "")
    {
        return TryGet(m_strings, path, m_viewModel.GetStringProperty, out ViewModelInstanceStringProperty prop)
            ? prop.Value
            : fallback;
    }

    public bool GetBool(string path, bool fallback = false)
    {
        return TryGet(m_bools, path, m_viewModel.GetBooleanProperty, out ViewModelInstanceBooleanProperty prop)
            ? prop.Value
            : fallback;
    }

    public string GetEnum(string path, string fallback = "")
    {
        return TryGet(m_enums, path, m_viewModel.GetEnumProperty, out ViewModelInstanceEnumProperty prop)
            ? prop.Value
            : fallback;
    }

    public UnityEngine.Color GetColor(string path)
    {
        return TryGet(m_colors, path, m_viewModel.GetColorProperty, out ViewModelInstanceColorProperty prop)
            ? prop.Value
            : UnityEngine.Color.clear;
    }

    // ── Observers (subscribe) ────────────────────────────────────────────────

    public void WatchNumber(string path, Action<float> onChange)
    {
        if (TryGet(m_numbers, path, m_viewModel.GetNumberProperty, out ViewModelInstanceNumberProperty prop))
            prop.OnValueChanged += onChange;
    }

    public void WatchString(string path, Action<string> onChange)
    {
        if (TryGet(m_strings, path, m_viewModel.GetStringProperty, out ViewModelInstanceStringProperty prop))
            prop.OnValueChanged += onChange;
    }

    public void WatchBool(string path, Action<bool> onChange)
    {
        if (TryGet(m_bools, path, m_viewModel.GetBooleanProperty, out ViewModelInstanceBooleanProperty prop))
            prop.OnValueChanged += onChange;
    }

    public void WatchColor(string path, Action<UnityEngine.Color> onChange)
    {
        if (TryGet(m_colors, path, m_viewModel.GetColorProperty, out ViewModelInstanceColorProperty prop))
            prop.OnValueChanged += onChange;
    }

    public void WatchEnum(string path, Action<string> onChange)
    {
        if (TryGet(m_enums, path, m_viewModel.GetEnumProperty, out ViewModelInstanceEnumProperty prop))
            prop.OnValueChanged += onChange;
    }

    public void WatchTrigger(string path, Action onFired)
    {
        if (TryGet(m_triggers, path, m_viewModel.GetTriggerProperty, out ViewModelInstanceTriggerProperty prop))
            prop.OnTriggered += onFired;
    }

    // ── Observers (unsubscribe) ──────────────────────────────────────────────

    public void UnwatchNumber(string path, Action<float> onChange)
    {
        if (m_numbers.TryGetValue(path, out ViewModelInstanceNumberProperty prop))
            prop.OnValueChanged -= onChange;
    }

    public void UnwatchString(string path, Action<string> onChange)
    {
        if (m_strings.TryGetValue(path, out ViewModelInstanceStringProperty prop))
            prop.OnValueChanged -= onChange;
    }

    public void UnwatchBool(string path, Action<bool> onChange)
    {
        if (m_bools.TryGetValue(path, out ViewModelInstanceBooleanProperty prop))
            prop.OnValueChanged -= onChange;
    }

    public void UnwatchColor(string path, Action<UnityEngine.Color> onChange)
    {
        if (m_colors.TryGetValue(path, out ViewModelInstanceColorProperty prop))
            prop.OnValueChanged -= onChange;
    }

    public void UnwatchEnum(string path, Action<string> onChange)
    {
        if (m_enums.TryGetValue(path, out ViewModelInstanceEnumProperty prop))
            prop.OnValueChanged -= onChange;
    }

    public void UnwatchTrigger(string path, Action onFired)
    {
        if (m_triggers.TryGetValue(path, out ViewModelInstanceTriggerProperty prop))
            prop.OnTriggered -= onFired;
    }

    // ── Raw property access (for types not covered above) ────────────────────

    /// <summary>Returns the cached number property handle, or null if not found.</summary>
    public ViewModelInstanceNumberProperty NumberProperty(string path)
    {
        TryGet(m_numbers, path, m_viewModel.GetNumberProperty, out ViewModelInstanceNumberProperty prop);
        return prop;
    }

    /// <summary>Returns the cached trigger property handle, or null if not found.</summary>
    public ViewModelInstanceTriggerProperty TriggerProperty(string path)
    {
        TryGet(m_triggers, path, m_viewModel.GetTriggerProperty, out ViewModelInstanceTriggerProperty prop);
        return prop;
    }

    /// <summary>
    /// Returns the list property at <paramref name="path"/> for manual list
    /// manipulation (Add, Remove, Insert, Swap, etc.).
    /// Result is NOT cached — store the reference yourself if calling repeatedly.
    /// </summary>
    public ViewModelInstanceListProperty GetListProperty(string path)
    {
        return m_viewModel?.GetListProperty(path);
    }

    /// <summary>
    /// Returns the image property at <paramref name="path"/> for runtime image swapping.
    /// Result is NOT cached — store the reference yourself if calling repeatedly.
    /// </summary>
    public ViewModelInstanceImageProperty GetImageProperty(string path)
    {
        return m_viewModel?.GetImageProperty(path);
    }

    /// <summary>
    /// Returns the artboard property at <paramref name="path"/> for runtime artboard swapping.
    /// Result is NOT cached — store the reference yourself if calling repeatedly.
    /// </summary>
    public ViewModelInstanceArtboardProperty GetArtboardProperty(string path)
    {
        return m_viewModel?.GetArtboardProperty(path);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private bool TryGet<T>(
        Dictionary<string, T> cache,
        string path,
        Func<string, T> fetch,
        out T property
    ) where T : class
    {
        if (m_viewModel == null)
        {
            property = null;
            return false;
        }

        if (!cache.TryGetValue(path, out property))
        {
            property = fetch(path);
            if (property != null)
                cache[path] = property;
        }

        return property != null;
    }
}
