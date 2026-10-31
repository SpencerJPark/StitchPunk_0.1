// Adds PAINTED_ENTITIES to the standalone defines when Entities Graphics is installed, so a fresh checkout
// bakes decals with their per-instance atlas tile and tint instead of drawing every decal with tile 0.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PaintedGround
{
    [InitializeOnLoad]
    public static class PaintedEntitiesDefine
    {
        const string DefineSymbol = "PAINTED_ENTITIES";

        static PaintedEntitiesDefine()
        {
            if (!System.IO.Directory.Exists("Packages/com.unity.entities.graphics") && !PackageCachePresent()) return;

            NamedBuildTarget standalone = NamedBuildTarget.Standalone;
            string defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(standalone);
            if (defines.Contains(DefineSymbol)) return;
            UnityEditor.PlayerSettings.SetScriptingDefineSymbols(standalone, string.IsNullOrEmpty(defines) ? DefineSymbol : defines + ";" + DefineSymbol);
            Debug.Log("[Painted Ground] Added " + DefineSymbol + " to the Standalone scripting defines (Entities Graphics found).");
        }

        static bool PackageCachePresent()
        {
            string[] matches = System.IO.Directory.Exists("Library/PackageCache")
                ? System.IO.Directory.GetDirectories("Library/PackageCache", "com.unity.entities.graphics@*")
                : new string[0];
            return matches.Length > 0;
        }
    }
}
#endif
