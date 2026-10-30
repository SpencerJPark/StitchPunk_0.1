using UnityEngine;
using UnityEngine.InputSystem;

// One action shows and hides every OnGUI debug menu at once; each menu polls it from Update.
public static class DebugMenuVisibility
{
    // Its own map so it works whichever gameplay map PlayerInput has switched to (backquote, or L3 + R3 on a gamepad).
    private const string ToggleDebugMenuActionPath = "Debug/ToggleDebugMenu";

    public static bool AreDebugMenusVisible { get; private set; } = true;

    private static int lastPolledFrame = -1;
    private static InputActionAsset resolvedFromAsset;
    private static InputAction toggleDebugMenuAction;

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
    }
}
