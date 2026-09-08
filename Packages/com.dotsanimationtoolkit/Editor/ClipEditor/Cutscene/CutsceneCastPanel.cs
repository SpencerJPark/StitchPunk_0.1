// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The cutscene's own view of the scene: one row per slot, its binding state, and Place/Bind/Select/Frame.</summary>
    internal sealed class CutsceneCastPanel : VisualElement
    {
        private enum BindingState
        {
            Unbound,
            Bound,
            Broken
        }

        private const string PlaceIconName = "d_Toolbar Plus";
        private const string BindIconName = "d_Linked";
        private const string SelectIconName = "d_UnityEditor.SceneHierarchyWindow";
        private const string FrameIconName = "d_ViewToolZoom";

        private readonly VisualElement rowsContainer = new VisualElement();
        private readonly Label stageStatusLabel = new Label();
        private readonly Button syncToStageButton;

        // Slot indices whose bind field the author opened. Survives Rebuild so a rebuild triggered
        // from elsewhere does not close the field out from under a drag-and-drop.
        private readonly HashSet<int> slotIndicesShowingBindField = new HashSet<int>();

        /// <summary>Raised with the slot index whose prefab should be instantiated and bound.</summary>
        public event Action<int> PlaceRequested;

        /// <summary>Raised with a slot index and the GameObject to bind to it, or null to unbind.</summary>
        public event Action<int, GameObject> BindRequested;

        /// <summary>Raised with the slot index whose row was clicked.</summary>
        public event Action<int> SlotSelected;

        /// <summary>Raised with the slot index whose bound object should be framed in the Scene view.</summary>
        public event Action<int> FrameRequested;

        /// <summary>Raised when the author presses Sync to Stage.</summary>
        public event Action SyncToStageRequested;

        /// <summary>Raised with the kind of slot to append.</summary>
        public event Action<CutsceneSlotKind> AddSlotRequested;

        public CutsceneCastPanel()
        {
            style.minWidth = 200f;
            style.paddingLeft = 6f;
            style.paddingTop = 6f;
            style.paddingRight = 4f;

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-pane-header");

            Label heading = new Label("Cast");
            heading.AddToClassList("toolkit-pane-title");
            headerRow.Add(heading);

            stageStatusLabel.style.marginLeft = 6f;
            stageStatusLabel.AddToClassList("cutscene-editor__cast-status");
            headerRow.Add(stageStatusLabel);

            VisualElement actionsRow = new VisualElement();
            actionsRow.AddToClassList("toolkit-pane-actions");

            Button addActorButton = ToolkitIcons.MakeIconButton(
                () => AddSlotRequested?.Invoke(CutsceneSlotKind.Actor), ToolkitIcons.Plus,
                "Add an actor slot.", "+ Actor");
            addActorButton.text = "Actor";
            addActorButton.AddToClassList("toolkit-icon-button--with-text");
            addActorButton.AddToClassList("toolkit-pane-action");
            actionsRow.Add(addActorButton);

            Button addPropButton = ToolkitIcons.MakeIconButton(
                () => AddSlotRequested?.Invoke(CutsceneSlotKind.Prop), ToolkitIcons.Plus,
                "Add a prop slot.", "+ Prop");
            addPropButton.text = "Prop";
            addPropButton.AddToClassList("toolkit-icon-button--with-text");
            addPropButton.AddToClassList("toolkit-pane-action");
            actionsRow.Add(addPropButton);

            syncToStageButton = new Button(() => SyncToStageRequested?.Invoke()) { text = "Sync to Stage" };
            syncToStageButton.tooltip =
                "Writes every bound slot into this scene's CutsceneStageAuthoring component, baking "
                + "one CutsceneStage entity that plays this cutscene at runtime. Explicit, never "
                + "automatic — press it after the cast is the way you want it.";
            syncToStageButton.AddToClassList("toolkit-pane-action");
            actionsRow.Add(syncToStageButton);

            headerRow.Add(actionsRow);

            Add(headerRow);

            ScrollView rowsScroll = new ScrollView(ScrollViewMode.Vertical);
            rowsScroll.style.flexGrow = 1f;
            rowsScroll.Add(rowsContainer);
            Add(rowsScroll);
        }

        /// <summary>Sets the Stage status text. Sync is explicit, so this only ever reports state — it never triggers a write.</summary>
        public void SetStageStatus(string statusText)
        {
            stageStatusLabel.text = statusText;
        }

        /// <summary>Rebuilds every row from the cutscene's current slots and bindings.</summary>
        /// <param name="selectedSlotIndex">The slot the timeline currently has selected, or −1.</param>
        public void Rebuild(CutsceneAsset cutscene, string currentSceneGuid, int selectedSlotIndex)
        {
            rowsContainer.Clear();

            if (cutscene == null || cutscene.slots == null || cutscene.slots.Count == 0)
            {
                slotIndicesShowingBindField.Clear();
                rowsContainer.Add(new Label("No slots yet — add an Actor or Prop slot.")
                { style = { whiteSpace = WhiteSpace.Normal } });
                return;
            }

            int slotCount = cutscene.slots.Count;
            slotIndicesShowingBindField.RemoveWhere(openSlotIndex => openSlotIndex >= slotCount);

            bool sceneMatches = !string.IsNullOrEmpty(cutscene.sceneGuid)
                && currentSceneGuid == cutscene.sceneGuid;
            if (!sceneMatches)
            {
                rowsContainer.Add(new Label(
                    "Open the remembered scene to place or bind the cast. Timing edits still work.")
                { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 6f } });
            }

            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }
                rowsContainer.Add(BuildRow(
                    cutscene, currentSceneGuid, sceneMatches, slot, slotIndex, slotIndex == selectedSlotIndex));
            }
        }

        private VisualElement BuildRow(
            CutsceneAsset cutscene, string currentSceneGuid, bool sceneMatches,
            CutsceneSlot slot, int slotIndex, bool isSelected)
        {
            int capturedIndex = slotIndex;

            VisualElement row = new VisualElement();
            row.AddToClassList("cutscene-editor__cast-row");
            row.AddToClassList("toolkit-box");
            row.EnableInClassList("cutscene-editor__cast-row--selected", isSelected);
            row.EnableInClassList("toolkit-box--selected", isSelected);

            GameObject boundObject;
            BindingState state = ResolveBindingState(cutscene, currentSceneGuid, slot, out boundObject);

            VisualElement line = new VisualElement();
            line.AddToClassList("cutscene-editor__cast-line");
            line.AddToClassList("toolkit-box__header");

            VisualElement identity = new VisualElement();
            identity.AddToClassList("cutscene-editor__cast-identity");
            // Registered on the identity half only, not the whole line: a pointer-down on the
            // buttons bubbles up just the same, and selecting on every such click tore the whole
            // cast panel down mid-interaction (SelectSlotHeader -> RefreshCastPanel -> Rebuild),
            // destroying the bind field before a drag-and-drop could commit.
            identity.RegisterCallback<PointerDownEvent>(_ => SlotSelected?.Invoke(capturedIndex));

            Label stateDot = new Label(StateGlyph(state));
            stateDot.AddToClassList("cutscene-editor__cast-dot");
            stateDot.AddToClassList(StateDotModifierClass(state));
            stateDot.tooltip = StateTooltip(state, boundObject);
            identity.Add(stateDot);

            Label nameLabel = new Label(slot.name);
            nameLabel.AddToClassList("cutscene-editor__cast-name");
            nameLabel.AddToClassList("toolkit-box__title");
            nameLabel.tooltip = slot.name;
            identity.Add(nameLabel);

            Label kindChip = new Label(slot.kind.ToString());
            kindChip.AddToClassList("cutscene-editor__cast-chip");
            kindChip.AddToClassList(slot.kind == CutsceneSlotKind.Prop
                ? "cutscene-editor__cast-chip--prop"
                : "cutscene-editor__cast-chip--actor");
            identity.Add(kindChip);

            line.Add(identity);

            VisualElement bindFieldRow = new VisualElement();
            bindFieldRow.AddToClassList("cutscene-editor__cast-bind-field");
            bindFieldRow.AddToClassList("toolkit-box__row");
            bindFieldRow.style.display = slotIndicesShowingBindField.Contains(slotIndex)
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            ObjectField bindField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = boundObject
            };
            bindField.SetEnabled(sceneMatches);
            bindField.RegisterValueChangedCallback(
                changeEvent => BindRequested?.Invoke(capturedIndex, changeEvent.newValue as GameObject));
            bindFieldRow.Add(bindField);

            Button placeButton = BuildIconButton(
                () => PlaceRequested?.Invoke(capturedIndex), PlaceIconName, "Place",
                slot.actorPrefab == null
                    ? "Place: assign an Actor Prefab on the slot first — Place instantiates it into "
                        + "the scene and binds it."
                    : "Place: instantiates '" + slot.actorPrefab.name + "' at the Scene view pivot "
                        + "and binds it to this slot.");
            // Placing over a live binding is how a slot silently ends up with two actors in the
            // scene and only one of them animating.
            placeButton.SetEnabled(sceneMatches && slot.actorPrefab != null && boundObject == null);
            line.Add(placeButton);

            Button bindButton = BuildIconButton(
                () => ToggleBindField(capturedIndex, bindFieldRow), BindIconName, "Bind",
                "Bind: opens this slot's object field — drag a scene object in to bind it, or clear "
                + "the field to unbind.");
            bindButton.SetEnabled(sceneMatches);
            line.Add(bindButton);

            Button selectButton = BuildIconButton(
                () => SlotSelected?.Invoke(capturedIndex), SelectIconName, "Select",
                "Select: selects this slot's bound object, in the timeline and in the hierarchy.");
            selectButton.SetEnabled(boundObject != null);
            line.Add(selectButton);

            Button frameButton = BuildIconButton(
                () => FrameRequested?.Invoke(capturedIndex), FrameIconName, "Frame",
                "Frame: points the Scene view camera at this slot's bound object.");
            frameButton.SetEnabled(boundObject != null);
            line.Add(frameButton);

            row.Add(line);
            row.Add(bindFieldRow);
            return row;
        }

        // Shows or hides one row's bind field in place. Never rebuilds the panel: a rebuild here
        // would destroy the field the author just asked for.
        private void ToggleBindField(int slotIndex, VisualElement bindFieldRow)
        {
            bool shouldShow = !slotIndicesShowingBindField.Contains(slotIndex);
            if (shouldShow)
            {
                slotIndicesShowingBindField.Add(slotIndex);
            }
            else
            {
                slotIndicesShowingBindField.Remove(slotIndex);
            }
            bindFieldRow.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static Button BuildIconButton(
            Action onClick, string iconName, string fallbackText, string tooltip)
        {
            Button button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("cutscene-editor__cast-button");

            Texture iconTexture = LoadEditorIconTexture(iconName);
            if (iconTexture == null)
            {
                // A built-in icon name that stops resolving must cost the author the picture, never
                // the button.
                button.text = fallbackText;
                button.AddToClassList("cutscene-editor__cast-button--text");
                return button;
            }

            Image icon = new Image { image = iconTexture, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("cutscene-editor__cast-icon");
            button.Add(icon);
            return button;
        }

        private static Texture LoadEditorIconTexture(string iconName)
        {
            Texture darkSkinTexture = LoadEditorIconTextureByExactName(iconName);
            if (darkSkinTexture != null || !iconName.StartsWith("d_", StringComparison.Ordinal))
            {
                return darkSkinTexture;
            }
            // Not every built-in icon ships a dark-skin variant, and asking for one that does not
            // exist yields nothing rather than the light original.
            return LoadEditorIconTextureByExactName(iconName.Substring(2));
        }

        private static Texture LoadEditorIconTextureByExactName(string iconName)
        {
            try
            {
                GUIContent iconContent = EditorGUIUtility.IconContent(iconName);
                return iconContent != null ? iconContent.image : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BindingState ResolveBindingState(
            CutsceneAsset cutscene, string currentSceneGuid, CutsceneSlot slot, out GameObject boundObject)
        {
            boundObject = null;
            CutsceneSlotBindingEntry entry =
                CutsceneSceneBinding.FindBinding(cutscene, currentSceneGuid, slot.SlotId);
            if (entry == null || string.IsNullOrEmpty(entry.globalObjectId))
            {
                return BindingState.Unbound;
            }
            boundObject = CutsceneSceneBinding.ResolveGameObject(entry.globalObjectId);
            return boundObject != null ? BindingState.Bound : BindingState.Broken;
        }

        private static string StateGlyph(BindingState state)
        {
            switch (state)
            {
                case BindingState.Bound:
                    return "●";
                case BindingState.Broken:
                    return "⚠";
                default:
                    return "○";
            }
        }

        private static string StateDotModifierClass(BindingState state)
        {
            switch (state)
            {
                case BindingState.Bound:
                    return "cutscene-editor__cast-dot--bound";
                case BindingState.Broken:
                    return "cutscene-editor__cast-dot--broken";
                default:
                    return "cutscene-editor__cast-dot--unbound";
            }
        }

        private static string StateTooltip(BindingState state, GameObject boundObject)
        {
            switch (state)
            {
                case BindingState.Bound:
                    // The row no longer shows the object field, so the dot is where the bound
                    // object's name lives.
                    return "Bound to '" + (boundObject != null ? boundObject.name : string.Empty)
                        + "' in this scene.";
                case BindingState.Broken:
                    return "Bound to an object this scene no longer has — re-bind or place again.";
                default:
                    return "Not bound yet — place a prefab or drag a scene object in.";
            }
        }

        // The slot whose bound object is `selected` or an ancestor of it, or −1. Walks up the
        // hierarchy because clicking a character in the Scene view usually selects a part, not the root.
        public static int FindSlotIndexForSelection(
            CutsceneAsset cutscene, string currentSceneGuid, GameObject selected)
        {
            if (cutscene == null || cutscene.slots == null || selected == null)
            {
                return -1;
            }

            List<GameObject> ancestry = new List<GameObject>();
            Transform walk = selected.transform;
            while (walk != null)
            {
                ancestry.Add(walk.gameObject);
                walk = walk.parent;
            }

            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }
                GameObject boundObject;
                if (ResolveBindingState(cutscene, currentSceneGuid, slot, out boundObject) != BindingState.Bound)
                {
                    continue;
                }
                if (ancestry.Contains(boundObject))
                {
                    return slotIndex;
                }
            }
            return -1;
        }
    }
}
