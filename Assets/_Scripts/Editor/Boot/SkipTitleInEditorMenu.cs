using UnityEditor;

public static class SkipTitleInEditorMenu
{
    public const string EditorPrefsKey = "StitchPunk.SkipTitleInEditor";
    private const string MenuPath = "StitchPunk/Skip Title in Editor";

    [InitializeOnLoadMethod]
    private static void ApplyCheckmarkOnLoad()
    {
        EditorApplication.delayCall += () => Menu.SetChecked(MenuPath, EditorPrefs.GetBool(EditorPrefsKey, false));
    }

    [MenuItem(MenuPath, false, 200)]
    private static void Toggle()
    {
        bool current = EditorPrefs.GetBool(EditorPrefsKey, false);
        EditorPrefs.SetBool(EditorPrefsKey, !current);
        Menu.SetChecked(MenuPath, !current);
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(EditorPrefsKey, false));
        return true;
    }
}
