using UnityEditor;
using UnityEngine;

namespace PlaytestCopilot.Editor
{
    /// Caches the AI-Play toolbar icon so the toolbar's per-domain-reload calls do not hit
    /// AssetDatabase every frame; the cache is invalidated only when the editor skin changes.
    public static class PlaytestIconLoader
    {
        private const string IconFolderRelativePath = "Editor/Icons";
        private const string FallbackPackageAssetPath = "Packages/com.playtestcopilot";
        private const string DarkSkinIconName = "d_PlayAI";
        private const string LightSkinIconName = "PlayAI";
        private const string RetinaSuffix = "@2x";

        private static Texture2D cachedIcon;
        private static bool hasCachedIcon;
        private static bool cachedIsProSkin;
        private static string resolvedPackageAssetPath;

        public static Texture2D LoadAIPlayIcon()
        {
            bool isProSkin = EditorGUIUtility.isProSkin;
            if (hasCachedIcon && cachedIsProSkin == isProSkin)
            {
                return cachedIcon;
            }

            cachedIcon = ResolveIcon(isProSkin);
            cachedIsProSkin = isProSkin;
            hasCachedIcon = true;
            return cachedIcon;
        }

        // Falls back in order: retina of the chosen skin, non-retina of the chosen skin, retina
        // of the other skin, non-retina of the other skin, then null — never throws or logs since
        // this runs on every domain reload.
        private static Texture2D ResolveIcon(bool isProSkin)
        {
            string primarySkinName = isProSkin ? DarkSkinIconName : LightSkinIconName;
            string secondarySkinName = isProSkin ? LightSkinIconName : DarkSkinIconName;
            bool wantsRetina = EditorGUIUtility.pixelsPerPoint > 1f;

            Texture2D icon = TryLoadIconVariant(primarySkinName, wantsRetina);
            if (icon != null)
            {
                return icon;
            }

            icon = TryLoadIconVariant(secondarySkinName, wantsRetina);
            return icon;
        }

        private static Texture2D TryLoadIconVariant(string skinIconName, bool wantsRetina)
        {
            if (wantsRetina)
            {
                Texture2D retinaIcon = TryLoadIconAtPath(skinIconName + RetinaSuffix);
                if (retinaIcon != null)
                {
                    return retinaIcon;
                }
            }

            return TryLoadIconAtPath(skinIconName);
        }

        private static Texture2D TryLoadIconAtPath(string iconName)
        {
            string assetPath = PackageAssetPath() + "/" + IconFolderRelativePath + "/" + iconName + ".png";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// The AssetDatabase addresses a package by its **package name**, not its folder name, so an
        /// embedded package in `Packages/playtest-copilot/` is loaded from
        /// `Packages/com.playtestcopilot/`. Hardcoding either one breaks on a rename, so ask the
        /// package manager which path this assembly actually came from.
        private static string PackageAssetPath()
        {
            if (!string.IsNullOrEmpty(resolvedPackageAssetPath))
            {
                return resolvedPackageAssetPath;
            }

            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlaytestIconLoader).Assembly);
            resolvedPackageAssetPath = packageInfo != null && !string.IsNullOrEmpty(packageInfo.assetPath)
                ? packageInfo.assetPath
                : FallbackPackageAssetPath;
            return resolvedPackageAssetPath;
        }
    }
}
