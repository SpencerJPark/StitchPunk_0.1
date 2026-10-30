using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class RebindEntry
{
    public string mapName;
    public string label;
    public InputAction action;
    public int bindingIndex;
}

// Rebinding for the shared InputActionAsset. PlayerInputManager registers the asset in the game scene; overrides persist
// in UserSettings so they survive restarts and apply to every scene that registers the asset.
public static class InputRebinding
{
    public const string KeyboardMouseGroup = "Keyboard&Mouse";
    public const string GamepadGroup = "Gamepad";

    private const string UiMapName = "UI";
    private const string DebugMapName = "Debug";
    private const float RebindTimeoutSeconds = 15f;

    private static InputActionAsset asset;
    private static InputActionRebindingExtensions.RebindingOperation activeOperation;
    private static InputAction activeAction;
    private static bool activeActionWasEnabled;

    public static InputActionAsset Asset => asset;
    public static bool IsRebinding => activeOperation != null;

    // The cancel key (Escape) is also the menu's close key; the menu uses this to ignore that same press.
    public static int LastRebindEndFrame { get; private set; } = -10;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticStateForDisabledDomainReload()
    {
        asset = null;
        activeOperation = null;
        activeAction = null;
    }

    public static void Register(InputActionAsset actionAsset)
    {
        CancelActiveRebind();
        asset = actionAsset;
        if (asset == null)
            return;

        string overridesJson = UserSettings.BindingOverridesJson;
        if (!string.IsNullOrEmpty(overridesJson))
            asset.LoadBindingOverridesFromJson(overridesJson);
    }

    public static List<RebindEntry> GetEntries(string bindingGroup)
    {
        List<RebindEntry> entries = new List<RebindEntry>();
        if (asset == null)
            return entries;

        foreach (InputActionMap actionMap in asset.actionMaps)
        {
            if (actionMap.name == UiMapName || actionMap.name == DebugMapName)
                continue;

            foreach (InputAction action in actionMap.actions)
            {
                for (int bindingIndex = 0; bindingIndex < action.bindings.Count; bindingIndex++)
                {
                    InputBinding binding = action.bindings[bindingIndex];
                    if (binding.isComposite || !BelongsToGroup(binding, bindingGroup))
                        continue;

                    entries.Add(new RebindEntry
                    {
                        mapName = actionMap.name,
                        label = BuildLabel(action, binding),
                        action = action,
                        bindingIndex = bindingIndex,
                    });
                }
            }
        }
        return entries;
    }

    public static string GetDisplayString(RebindEntry entry)
    {
        string display = entry.action.GetBindingDisplayString(entry.bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        return string.IsNullOrEmpty(display) ? "Unbound" : display;
    }

    public static void StartRebind(RebindEntry entry, string bindingGroup, Action onFinished)
    {
        CancelActiveRebind();

        activeAction = entry.action;
        activeActionWasEnabled = entry.action.enabled;
        if (activeActionWasEnabled)
            entry.action.Disable();

        bool isGamepad = bindingGroup == GamepadGroup;
        InputActionRebindingExtensions.RebindingOperation operation = entry.action.PerformInteractiveRebinding(entry.bindingIndex)
            .WithCancelingThrough(isGamepad ? "<Gamepad>/start" : "<Keyboard>/escape")
            .WithTimeout(RebindTimeoutSeconds)
            .OnMatchWaitForAnother(0.1f);

        if (isGamepad)
        {
            operation.WithControlsHavingToMatchPath("<Gamepad>");
        }
        else
        {
            operation.WithControlsHavingToMatchPath("<Keyboard>");
            operation.WithControlsHavingToMatchPath("<Mouse>");
            operation.WithControlsExcluding("<Mouse>/position");
            operation.WithControlsExcluding("<Mouse>/delta");
        }

        operation.OnComplete(finishedOperation =>
        {
            FinishRebind(true);
            onFinished?.Invoke();
        });
        operation.OnCancel(cancelledOperation =>
        {
            FinishRebind(false);
            onFinished?.Invoke();
        });

        activeOperation = operation;
        operation.Start();
    }

    public static void CancelActiveRebind()
    {
        if (activeOperation == null)
            return;

        // Cancel() raises OnCancel, which runs FinishRebind and clears activeOperation.
        activeOperation.Cancel();
    }

    public static void ResetEntry(RebindEntry entry)
    {
        entry.action.RemoveBindingOverride(entry.bindingIndex);
        PersistOverrides();
    }

    public static void ResetAll()
    {
        CancelActiveRebind();
        if (asset == null)
            return;

        asset.RemoveAllBindingOverrides();
        PersistOverrides();
    }

    private static void FinishRebind(bool completed)
    {
        activeOperation?.Dispose();
        activeOperation = null;

        if (activeActionWasEnabled && activeAction != null)
            activeAction.Enable();
        activeAction = null;

        LastRebindEndFrame = Time.frameCount;
        if (completed)
            PersistOverrides();
    }

    private static void PersistOverrides()
    {
        UserSettings.BindingOverridesJson = asset == null ? "" : asset.SaveBindingOverridesAsJson();
    }

    private static bool BelongsToGroup(InputBinding binding, string bindingGroup)
    {
        return string.IsNullOrEmpty(binding.groups) || binding.groups.Contains(bindingGroup);
    }

    // "QuickCommand1" -> "Quick Command 1"
    public static string SplitWords(string identifier)
    {
        StringBuilder words = new StringBuilder();
        for (int characterIndex = 0; characterIndex < identifier.Length; characterIndex++)
        {
            char character = identifier[characterIndex];
            bool startsNewWord = characterIndex > 0
                && (char.IsUpper(character) || (char.IsDigit(character) && !char.IsDigit(identifier[characterIndex - 1])))
                && identifier[characterIndex - 1] != ' ';
            if (startsNewWord)
                words.Append(' ');
            words.Append(character);
        }
        return words.ToString();
    }

    // Composite parts append their part name ("Move up").
    private static string BuildLabel(InputAction action, InputBinding binding)
    {
        string label = SplitWords(action.name);
        return binding.isPartOfComposite ? label + " " + binding.name : label;
    }
}
