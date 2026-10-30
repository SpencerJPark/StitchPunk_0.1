using UnityEngine;
using UnityEngine.InputSystem;

// One key shows and hides every OnGUI debug menu at once; each menu polls it from Update.
public static class DebugMenuVisibility
{
    // Not F1: laptop keyboards send media keys on the F row unless Fn is held.
    public const Key ToggleKey = Key.Backquote;

    public static bool AreDebugMenusVisible { get; private set; } = true;

    private static int lastPolledFrame = -1;

    // Several menus poll in the same frame; only the first one may flip the state.
    public static void PollToggleKey()
    {
        if (lastPolledFrame == Time.frameCount)
            return;
        lastPolledFrame = Time.frameCount;

        if (Keyboard.current != null && Keyboard.current[ToggleKey].wasPressedThisFrame)
            AreDebugMenusVisible = !AreDebugMenusVisible;
    }

    // Statics survive entering Play mode when domain reload is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        AreDebugMenusVisible = true;
        lastPolledFrame = -1;
    }
}
