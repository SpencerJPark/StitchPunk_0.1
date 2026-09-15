// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The pick-a-target and confirm steps every refactor entry point shares; nothing runs until the dialog is confirmed.</summary>
    public static class RefactorPromptEditing
    {
        public static bool ConfirmAndRekeyEvent(uint fromKey, uint toKey)
        {
            return false;
        }

        public static bool ConfirmAndMergeEventKeys(uint fromKey, uint intoKey)
        {
            return false;
        }

        public static bool ConfirmAndReplaceTrackTag(uint fromTagId, uint toTagId)
        {
            return false;
        }

        public static void PickKeyThenRekeyEverywhere(VisualElement pickerHost, VisualElement anchor, uint fromKey, Action onApplied)
        {
        }

        public static void PickTagThenReplaceTrackTag(VisualElement pickerHost, VisualElement anchor, uint fromTagId, Action onApplied)
        {
        }

        public static void ShowMergeIntoMenu(VisualElement anchor, uint fromKey, Action onApplied)
        {
        }

        public static void ShowReplaceTagMenu(VisualElement anchor, uint fromTagId, Action onApplied)
        {
        }
    }
}
