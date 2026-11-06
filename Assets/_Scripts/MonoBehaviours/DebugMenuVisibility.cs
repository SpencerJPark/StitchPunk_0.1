using System.Collections.Generic;
using DotsMovementToolkit;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

// Everything debug is off by default; one action opens the DebugMenuPanel, which owns a toggle per registered tool.
public static class DebugMenuVisibility
{
    // Its own map so it works whichever gameplay map PlayerInput has switched to (backquote, or L3 + R3 on a gamepad).
    private const string ToggleDebugMenuActionPath = "Debug/ToggleDebugMenu";

    public static bool IsPanelOpen { get; private set; }

    public static NavGridDebugDisplayMode NavGridMode { get; set; } = NavGridDebugDisplayMode.Off;

    private static readonly List<string> registeredToggleNames = new List<string>();
    private static readonly Dictionary<string, bool> toggleValuesByName = new Dictionary<string, bool>();

    private static int lastPolledFrame = -1;
    private static InputActionAsset resolvedFromAsset;
    private static InputAction toggleDebugMenuAction;

    private static World navGridDebugQueryWorld;
    private static EntityQuery navGridDebugSettingsQuery;

    public static IReadOnlyList<string> RegisteredToggleNames => registeredToggleNames;

    public static string ToggleBindingDisplayText =>
        toggleDebugMenuAction != null ? toggleDebugMenuAction.GetBindingDisplayString() : "(no Debug/ToggleDebugMenu action)";

    public static bool IsToggleOn(string toggleName)
    {
        return toggleName != null && toggleValuesByName.TryGetValue(toggleName, out bool isOn) && isOn;
    }

    public static void RegisterToggle(string toggleName)
    {
        if (string.IsNullOrEmpty(toggleName) || toggleValuesByName.ContainsKey(toggleName))
            return;
        registeredToggleNames.Add(toggleName);
        toggleValuesByName[toggleName] = false;
    }

    public static void SetToggle(string toggleName, bool isOn)
    {
        if (string.IsNullOrEmpty(toggleName))
            return;
        RegisterToggle(toggleName);
        toggleValuesByName[toggleName] = isOn;
    }

    // Several callers poll in the same frame; only the first one may flip the state.
    public static void PollToggleAction()
    {
        if (lastPolledFrame == Time.frameCount)
            return;
        lastPolledFrame = Time.frameCount;

        ResolveToggleActionFromRegisteredAsset();
        if (toggleDebugMenuAction != null && toggleDebugMenuAction.WasPressedThisFrame())
            IsPanelOpen = !IsPanelOpen;

        EnforceNavGridMode();
    }

    // Every frame, not only on change: a subscene re-bake restores the authored display mode.
    private static void EnforceNavGridMode()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (navGridDebugQueryWorld != world)
        {
            navGridDebugQueryWorld = world;
            navGridDebugSettingsQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NavGridDebugSettings>());
        }

        if (!navGridDebugSettingsQuery.TryGetSingletonRW(out RefRW<NavGridDebugSettings> debugSettings))
            return;

        if (debugSettings.ValueRO.displayMode != NavGridMode)
            debugSettings.ValueRW.displayMode = NavGridMode;
    }

    // PlayerInputManager registers the scene's asset in Awake and disables every map there, so enable lazily on first poll.
    private static void ResolveToggleActionFromRegisteredAsset()
    {
        InputActionAsset registeredAsset = InputRebinding.Asset;
        if (registeredAsset == resolvedFromAsset)
        {
            if (toggleDebugMenuAction != null && !toggleDebugMenuAction.enabled && !InputRebinding.IsRebinding)
                toggleDebugMenuAction.Enable();
            return;
        }

        resolvedFromAsset = registeredAsset;
        toggleDebugMenuAction = registeredAsset != null ? registeredAsset.FindAction(ToggleDebugMenuActionPath) : null;
        toggleDebugMenuAction?.Enable();
    }

    // Statics survive entering Play mode when domain reload is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        IsPanelOpen = false;
        NavGridMode = NavGridDebugDisplayMode.Off;
        registeredToggleNames.Clear();
        toggleValuesByName.Clear();
        lastPolledFrame = -1;
        resolvedFromAsset = null;
        toggleDebugMenuAction = null;
        navGridDebugQueryWorld = null;
        navGridDebugSettingsQuery = default;
    }
}
