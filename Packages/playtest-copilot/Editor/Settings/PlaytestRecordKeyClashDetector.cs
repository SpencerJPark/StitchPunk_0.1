using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PlaytestCopilot.Editor
{
    /// One clash entry: a binding in a project .inputactions asset that already uses the
    /// candidate record key.
    [Serializable]
    public sealed class PlaytestKeyClash
    {
        public string AssetPath;
        public string ActionMapName;
        public string ActionName;
        public string BindingPath;
    }

    /// The package never references the Input System package, so clashes are found by reading
    /// every .inputactions asset as plain JSON text instead of through InputActionAsset.
    public static class PlaytestRecordKeyClashDetector
    {
        [Serializable]
        private sealed class InputActionBindingJson
        {
            public string path;
            public string action;
        }

        [Serializable]
        private sealed class InputActionMapJson
        {
            public string name;
            public List<InputActionBindingJson> bindings;
        }

        [Serializable]
        private sealed class InputActionAssetJson
        {
            public List<InputActionMapJson> maps;
        }

        public static List<PlaytestKeyClash> FindClashes(KeyCode key)
        {
            List<PlaytestKeyClash> foundClashes = new List<PlaytestKeyClash>();

            string targetBindingPath = InputSystemPathFor(key);
            // An unmapped key has no binding path to compare against; matching everything here
            // would be the inverted-check bug that makes the whole feature useless.
            if (string.IsNullOrEmpty(targetBindingPath))
            {
                return foundClashes;
            }

            string projectRootPath = Directory.GetParent(Application.dataPath).FullName;

            foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!assetPath.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string absoluteAssetPath = Path.Combine(projectRootPath, assetPath);
                string assetJsonText;
                try
                {
                    assetJsonText = File.ReadAllText(absoluteAssetPath);
                }
                catch (IOException)
                {
                    continue;
                }

                InputActionAssetJson parsedAsset;
                try
                {
                    parsedAsset = JsonUtility.FromJson<InputActionAssetJson>(assetJsonText);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (parsedAsset == null || parsedAsset.maps == null)
                {
                    continue;
                }

                foreach (InputActionMapJson actionMap in parsedAsset.maps)
                {
                    if (actionMap == null || actionMap.bindings == null)
                    {
                        continue;
                    }

                    string lastNonEmptyActionName = string.Empty;
                    foreach (InputActionBindingJson binding in actionMap.bindings)
                    {
                        if (binding == null)
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(binding.action))
                        {
                            lastNonEmptyActionName = binding.action;
                        }

                        if (string.IsNullOrEmpty(binding.path))
                        {
                            continue;
                        }

                        if (!string.Equals(binding.path, targetBindingPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        foundClashes.Add(new PlaytestKeyClash
                        {
                            AssetPath = assetPath,
                            ActionMapName = actionMap.name,
                            ActionName = lastNonEmptyActionName,
                            BindingPath = binding.path,
                        });
                    }
                }
            }

            return foundClashes;
        }

        public static string InputSystemPathFor(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Alpha0: return "<Keyboard>/0";
                case KeyCode.Alpha1: return "<Keyboard>/1";
                case KeyCode.Alpha2: return "<Keyboard>/2";
                case KeyCode.Alpha3: return "<Keyboard>/3";
                case KeyCode.Alpha4: return "<Keyboard>/4";
                case KeyCode.Alpha5: return "<Keyboard>/5";
                case KeyCode.Alpha6: return "<Keyboard>/6";
                case KeyCode.Alpha7: return "<Keyboard>/7";
                case KeyCode.Alpha8: return "<Keyboard>/8";
                case KeyCode.Alpha9: return "<Keyboard>/9";
                case KeyCode.F1: return "<Keyboard>/f1";
                case KeyCode.F2: return "<Keyboard>/f2";
                case KeyCode.F3: return "<Keyboard>/f3";
                case KeyCode.F4: return "<Keyboard>/f4";
                case KeyCode.F5: return "<Keyboard>/f5";
                case KeyCode.F6: return "<Keyboard>/f6";
                case KeyCode.F7: return "<Keyboard>/f7";
                case KeyCode.F8: return "<Keyboard>/f8";
                case KeyCode.F9: return "<Keyboard>/f9";
                case KeyCode.F10: return "<Keyboard>/f10";
                case KeyCode.F11: return "<Keyboard>/f11";
                case KeyCode.F12: return "<Keyboard>/f12";
                case KeyCode.BackQuote: return "<Keyboard>/backquote";
                case KeyCode.Space: return "<Keyboard>/space";
                case KeyCode.Tab: return "<Keyboard>/tab";
                case KeyCode.Return: return "<Keyboard>/enter";
                case KeyCode.Escape: return "<Keyboard>/escape";
                case KeyCode.LeftShift: return "<Keyboard>/leftShift";
                case KeyCode.RightShift: return "<Keyboard>/rightShift";
                case KeyCode.LeftControl: return "<Keyboard>/leftCtrl";
                case KeyCode.RightControl: return "<Keyboard>/rightCtrl";
                case KeyCode.LeftAlt: return "<Keyboard>/leftAlt";
                case KeyCode.RightAlt: return "<Keyboard>/rightAlt";
                case KeyCode.UpArrow: return "<Keyboard>/upArrow";
                case KeyCode.DownArrow: return "<Keyboard>/downArrow";
                case KeyCode.LeftArrow: return "<Keyboard>/leftArrow";
                case KeyCode.RightArrow: return "<Keyboard>/rightArrow";
                case KeyCode.Minus: return "<Keyboard>/minus";
                case KeyCode.Equals: return "<Keyboard>/equals";
                case KeyCode.LeftBracket: return "<Keyboard>/leftBracket";
                case KeyCode.RightBracket: return "<Keyboard>/rightBracket";
                case KeyCode.Semicolon: return "<Keyboard>/semicolon";
                case KeyCode.Quote: return "<Keyboard>/apostrophe";
                case KeyCode.Comma: return "<Keyboard>/comma";
                case KeyCode.Period: return "<Keyboard>/period";
                case KeyCode.Slash: return "<Keyboard>/slash";
                case KeyCode.Backslash: return "<Keyboard>/backslash";
                default:
                    if (key >= KeyCode.A && key <= KeyCode.Z)
                    {
                        char letter = (char)('a' + (key - KeyCode.A));
                        return "<Keyboard>/" + letter;
                    }

                    return string.Empty;
            }
        }
    }
}
