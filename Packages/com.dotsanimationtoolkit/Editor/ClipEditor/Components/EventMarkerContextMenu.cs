// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine;
using UnityEngine.UIElements;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One right-click menu for an event marker, shared by the clip timeline and the cutscene
    /// timeline. The payload clipboard here is separate from the timeline's key clipboard — it
    /// carries a marker's int/float payload between markers of either type, never a time.
    /// </summary>
    public static class EventMarkerContextMenu
    {
        private static int copiedIntParam;
        private static float copiedFloatParam;

        public static bool HasCopiedPayload { get; private set; }

        public static void Populate(
            DropdownMenu menu,
            IEventMarkerAccessor accessor,
            AnimEventKeyRegistry registry,
            Action openKeyPicker,
            Action changeKeyEverywhere,
            Action duplicateMarker,
            Action deleteMarker,
            Action<EventMarkerField> markerEdited,
            Action onRegistryClosed)
        {
            if (accessor == null || !accessor.MarkerExists)
            {
                return;
            }

            bool canRename = registry != null && registry.ContainsKey(accessor.Key);
            menu.AppendAction(
                "Rename key…",
                menuAction =>
                {
                    VocabularyPickerConfig config = VocabularyPickerConfig.ForEventKeys(registry);
                    VocabularyQuickEditWindow.Open(
                        config.QuickEditWindowTitle,
                        registry,
                        config.QuickEditMissingMessage,
                        onRegistryClosed);
                },
                canRename ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            menu.AppendAction(
                "Change key…",
                menuAction => openKeyPicker(),
                openKeyPicker != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            menu.AppendAction(
                "Change key everywhere…",
                menuAction => changeKeyEverywhere(),
                changeKeyEverywhere != null && accessor.Key != 0u
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled);

            menu.AppendAction(
                "Duplicate marker",
                menuAction => duplicateMarker(),
                duplicateMarker != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            menu.AppendAction(
                "Delete marker",
                menuAction => deleteMarker(),
                deleteMarker != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            menu.AppendSeparator();

            menu.AppendAction(
                "Copy payload",
                menuAction =>
                {
                    copiedIntParam = accessor.IntParam;
                    copiedFloatParam = accessor.FloatParam;
                    HasCopiedPayload = true;
                });

            menu.AppendAction(
                "Paste payload",
                menuAction =>
                {
                    accessor.IntParam = copiedIntParam;
                    accessor.FloatParam = copiedFloatParam;
                    markerEdited?.Invoke(EventMarkerField.IntParam);
                },
                HasCopiedPayload ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }
    }
}
