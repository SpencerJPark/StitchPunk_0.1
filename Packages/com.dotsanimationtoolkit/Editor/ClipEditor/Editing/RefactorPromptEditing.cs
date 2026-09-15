// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The pick-a-target and confirm steps every refactor entry point shares; nothing runs until the dialog is confirmed.</summary>
    public static class RefactorPromptEditing
    {
        public static bool ConfirmAndRekeyEvent(uint fromKey, uint toKey)
        {
            if (fromKey == 0u || toKey == 0u || fromKey == toKey)
            {
                return false;
            }

            string fromName = EventDisplayName(fromKey);
            string toName = EventDisplayName(toKey);
            List<AssetReference> references = RefactorEditing.PreviewRekeyEvent(fromKey);
            if (references.Count == 0)
            {
                EditorUtility.DisplayDialog("Change Key Everywhere", "Nothing uses '" + fromName + "' yet, so there is nothing to change.", "OK");
                return false;
            }

            string summary = AssetReferenceIndex.SummarizeForDialog(references);
            bool confirmed = EditorUtility.DisplayDialog("Change Key Everywhere", "Change every use of '" + fromName + "' to '" + toName + "'?\n\n" + summary + "\n\nOne undo step reverts every asset.", "Change", "Cancel");
            if (!confirmed)
            {
                return false;
            }

            RefactorEditing.RekeyEvent(fromKey, toKey);
            return true;
        }

        public static bool ConfirmAndMergeEventKeys(uint fromKey, uint intoKey)
        {
            if (fromKey == 0u || intoKey == 0u || fromKey == intoKey)
            {
                return false;
            }

            string fromName = EventDisplayName(fromKey);
            string intoName = EventDisplayName(intoKey);
            List<AssetReference> references = RefactorEditing.PreviewRekeyEvent(fromKey);

            string message = "Merge '" + fromName + "' into '" + intoName + "'?\n\n";
            if (references.Count == 0)
            {
                message += "Nothing uses '" + fromName + "'; merging only removes it from the event list.";
            }
            else
            {
                message += AssetReferenceIndex.SummarizeForDialog(references) + "\n\nEvery use moves to '" + intoName + "' and '" + fromName + "' is removed from the event list. One undo step reverts all of it.";
            }

            AnimEventKeyRegistry animEventKeys = VocabularyRegistryProvider.AnimEventKeys;
            AnimEventKeyEntry fromEntry = FindEventEntry(animEventKeys, fromKey);
            AnimEventKeyEntry intoEntry = FindEventEntry(animEventKeys, intoKey);
            if (!RefactorTargetResolver.PayloadSchemasMatch(fromEntry, intoEntry))
            {
                message += "\n\nThese two events describe their payloads differently. Markers keep their int and float values as they are; nothing is remapped by name.";
            }

            bool confirmed = EditorUtility.DisplayDialog("Merge Events", message, "Merge", "Cancel");
            if (!confirmed)
            {
                return false;
            }

            RefactorEditing.MergeEventKeys(fromKey, intoKey);
            return true;
        }

        public static bool ConfirmAndReplaceTrackTag(uint fromTagId, uint toTagId)
        {
            if (fromTagId == 0u || toTagId == 0u || fromTagId == toTagId)
            {
                return false;
            }

            string fromName = TagDisplayName(fromTagId);
            string toName = TagDisplayName(toTagId);
            List<AssetReference> references = RefactorEditing.PreviewReplaceTrackTag(fromTagId);
            if (references.Count == 0)
            {
                EditorUtility.DisplayDialog("Move Tracks to Another Tag", "No clip or cutscene track uses '" + fromName + "'.", "OK");
                return false;
            }

            string summary = AssetReferenceIndex.SummarizeForDialog(references);
            bool confirmed = EditorUtility.DisplayDialog("Move Tracks to Another Tag", "Move every track on '" + fromName + "' to '" + toName + "'?\n\n" + summary + "\n\nRig targets keep their tags. One undo step reverts every asset.", "Move", "Cancel");
            if (!confirmed)
            {
                return false;
            }

            RefactorEditing.ReplaceTrackTag(fromTagId, toTagId);
            return true;
        }

        public static void PickKeyThenRekeyEverywhere(VisualElement pickerHost, VisualElement anchor, uint fromKey, Action onApplied)
        {
            if (pickerHost == null || fromKey == 0u)
            {
                return;
            }

            AnimEventKeyRegistry registry = VocabularyRegistryProvider.AnimEventKeys;
            VocabularyPicker.Open(pickerHost, anchor, registry, registry, VocabularyPickerConfig.ForEventKeys(registry), chosenKey =>
            {
                if (ConfirmAndRekeyEvent(fromKey, chosenKey))
                {
                    onApplied?.Invoke();
                }
            }, () => { });
        }

        public static void PickTagThenReplaceTrackTag(VisualElement pickerHost, VisualElement anchor, uint fromTagId, Action onApplied)
        {
            if (pickerHost == null || fromTagId == 0u)
            {
                return;
            }

            TargetTagRegistry registry = VocabularyRegistryProvider.TargetTags;
            VocabularyPicker.Open(pickerHost, anchor, registry, registry, VocabularyPickerConfig.ForTrackTagRebind(registry), chosenTagId =>
            {
                if (ConfirmAndReplaceTrackTag(fromTagId, chosenTagId))
                {
                    onApplied?.Invoke();
                }
            }, () => { });
        }

        public static void ShowMergeIntoMenu(VisualElement anchor, uint fromKey, Action onApplied)
        {
            if (anchor == null || fromKey == 0u)
            {
                return;
            }

            AnimEventKeyRegistry registry = VocabularyRegistryProvider.AnimEventKeys;
            List<AnimEventKeyEntry> candidates = new List<AnimEventKeyEntry>();
            if (registry.entries != null)
            {
                foreach (AnimEventKeyEntry entry in registry.entries)
                {
                    if (entry != null && entry.eventKey != 0u && entry.eventKey != fromKey && !string.IsNullOrEmpty(entry.name))
                    {
                        candidates.Add(entry);
                    }
                }
            }
            candidates.Sort((firstEntry, secondEntry) => string.Compare(firstEntry.name, secondEntry.name, StringComparison.OrdinalIgnoreCase));

            if (candidates.Count == 0)
            {
                EditorUtility.DisplayDialog("Merge Events", "There is no other event to merge into.", "OK");
                return;
            }

            GenericDropdownMenu menu = new GenericDropdownMenu();
            foreach (AnimEventKeyEntry candidate in candidates)
            {
                uint intoKey = candidate.eventKey;
                menu.AddItem(candidate.name, false, () =>
                {
                    if (ConfirmAndMergeEventKeys(fromKey, intoKey))
                    {
                        onApplied?.Invoke();
                    }
                });
            }
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        public static void ShowReplaceTagMenu(VisualElement anchor, uint fromTagId, Action onApplied)
        {
            if (anchor == null || fromTagId == 0u)
            {
                return;
            }

            TargetTagRegistry registry = VocabularyRegistryProvider.TargetTags;
            List<TargetTagEntry> candidates = new List<TargetTagEntry>();
            if (registry.entries != null)
            {
                foreach (TargetTagEntry entry in registry.entries)
                {
                    if (entry != null && entry.stableId != 0u && entry.stableId != fromTagId && !string.IsNullOrEmpty(entry.name))
                    {
                        candidates.Add(entry);
                    }
                }
            }
            candidates.Sort((firstEntry, secondEntry) => string.Compare(firstEntry.name, secondEntry.name, StringComparison.OrdinalIgnoreCase));

            if (candidates.Count == 0)
            {
                EditorUtility.DisplayDialog("Move Tracks to Another Tag", "There is no other tag to move tracks to.", "OK");
                return;
            }

            GenericDropdownMenu menu = new GenericDropdownMenu();
            foreach (TargetTagEntry candidate in candidates)
            {
                uint toTagId = candidate.stableId;
                menu.AddItem(candidate.name, false, () =>
                {
                    if (ConfirmAndReplaceTrackTag(fromTagId, toTagId))
                    {
                        onApplied?.Invoke();
                    }
                });
            }
            menu.DropDown(anchor.worldBound, anchor, DropdownMenuSizeMode.Auto);
        }

        private static string EventDisplayName(uint eventKey)
        {
            return VocabularyRegistryProvider.AnimEventKeys.FindName(eventKey) ?? "(unresolved event)";
        }

        private static string TagDisplayName(uint tagId)
        {
            return VocabularyRegistryProvider.TargetTags.FindName(tagId) ?? "(unresolved 0x" + tagId.ToString("X8") + ")";
        }

        private static AnimEventKeyEntry FindEventEntry(AnimEventKeyRegistry registry, uint eventKey)
        {
            if (registry.entries == null)
            {
                return null;
            }

            foreach (AnimEventKeyEntry entry in registry.entries)
            {
                if (entry != null && entry.eventKey == eventKey)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
