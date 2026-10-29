using DotsAnimationToolkit.Authoring;
#if UNITY_EDITOR
using System.IO;
#endif

// Bake-time animation name → key lookup through the toolkit's AnimationNameRegistry (G5 P2). The
// registry's provider lives in DotsAnimationToolkit.Editor, which runtime assemblies can't reference,
// so this reads the same ProjectSettings JSON directly. Read side only; nothing here writes the file.
public static class AnimationNameKeys
{
#if UNITY_EDITOR
    private const string AnimationNameRegistryPath = "ProjectSettings/DotsAnimationToolkitAnimationNameRegistry.asset";
    private static AnimationNameRegistry cachedAnimationNameRegistry;
    private static System.DateTime cachedRegistryWriteTime;
#endif

    // 0 when the name isn't registered (or outside the Editor, where nothing bakes).
    public static uint Resolve(string animationName)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(animationName)) return 0;
        AnimationNameRegistry registry = LoadAnimationNameRegistry();
        if (registry.entries == null) return 0;
        for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
        {
            AnimationNameEntry entry = registry.entries[entryIndex];
            if (entry != null && entry.name == animationName) return entry.animationKey;
        }
#endif
        return 0;
    }

#if UNITY_EDITOR
    // Re-read when the file changes, so a name added in the Actor Editor resolves on the next bake, not the next domain reload.
    private static AnimationNameRegistry LoadAnimationNameRegistry()
    {
        System.DateTime registryWriteTime = File.Exists(AnimationNameRegistryPath)
            ? File.GetLastWriteTimeUtc(AnimationNameRegistryPath)
            : System.DateTime.MinValue;
        if (cachedAnimationNameRegistry != null && registryWriteTime == cachedRegistryWriteTime) return cachedAnimationNameRegistry;

        if (cachedAnimationNameRegistry == null)
        {
            cachedAnimationNameRegistry = UnityEngine.ScriptableObject.CreateInstance<AnimationNameRegistry>();
            cachedAnimationNameRegistry.hideFlags = UnityEngine.HideFlags.DontSave;
        }
        cachedAnimationNameRegistry.entries = new System.Collections.Generic.List<AnimationNameEntry>();
        if (File.Exists(AnimationNameRegistryPath))
            UnityEditor.EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(AnimationNameRegistryPath), cachedAnimationNameRegistry);
        cachedRegistryWriteTime = registryWriteTime;
        return cachedAnimationNameRegistry;
    }
#endif
}
