using DotsMovementToolkit;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

// One action shows and hides every OnGUI debug menu, plus the nav-grid debug tiles, at once; each menu polls it from Update.
public static class DebugMenuVisibility
{
    // Its own map so it works whichever gameplay map PlayerInput has switched to (backquote, or L3 + R3 on a gamepad).
    private const string ToggleDebugMenuActionPath = "Debug/ToggleDebugMenu";

    public static bool AreDebugMenusVisible { get; private set; } = true;

    private static int lastPolledFrame = -1;
    private static InputActionAsset resolvedFromAsset;
    private static InputAction toggleDebugMenuAction;

    private static World navGridDebugQueryWorld;
    private static EntityQuery navGridDebugSettingsQuery;
    private static NavGridDebugDisplayMode navGridDisplayModeBeforeHiding = NavGridDebugDisplayMode.Off;

    public static string ToggleBindingDisplayText =>
        toggleDebugMenuAction != null ? toggleDebugMenuAction.GetBindingDisplayString() : "(no Debug/ToggleDebugMenu action)";

    // Several menus poll in the same frame; only the first one may flip the state.
    public static void PollToggleAction()
    {
        if (lastPolledFrame == Time.frameCount)
            return;
        lastPolledFrame = Time.frameCount;

        ResolveToggleActionFromRegisteredAsset();
        if (toggleDebugMenuAction != null && toggleDebugMenuAction.WasPressedThisFrame())
            AreDebugMenusVisible = !AreDebugMenusVisible;

        ApplyVisibilityToNavGridDebugTiles();
    }

    // Enforced every frame, not only on toggle: a subscene re-bake restores the authored display mode.
    private static void ApplyVisibilityToNavGridDebugTiles()
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

        if (!AreDebugMenusVisible && debugSettings.ValueRO.displayMode != NavGridDebugDisplayMode.Off)
        {
            navGridDisplayModeBeforeHiding = debugSettings.ValueRO.displayMode;
            debugSettings.ValueRW.displayMode = NavGridDebugDisplayMode.Off;
        }
        else if (AreDebugMenusVisible && navGridDisplayModeBeforeHiding != NavGridDebugDisplayMode.Off)
        {
            debugSettings.ValueRW.displayMode = navGridDisplayModeBeforeHiding;
            navGridDisplayModeBeforeHiding = NavGridDebugDisplayMode.Off;
        }
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
        AreDebugMenusVisible = true;
        lastPolledFrame = -1;
        resolvedFromAsset = null;
        toggleDebugMenuAction = null;
        navGridDebugQueryWorld = null;
        navGridDisplayModeBeforeHiding = NavGridDebugDisplayMode.Off;
    }
}
