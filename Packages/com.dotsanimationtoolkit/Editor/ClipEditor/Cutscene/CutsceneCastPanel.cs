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
    /// <summary>
    /// The cutscene's own view of the scene (amendment A58 §3.3): one row per slot, its binding
    /// state, and the four things an author does with it — Place, Bind, Select, Frame.
    /// </summary>
    /// <remarks>
    /// It does not replace Unity's Hierarchy or Inspector; selecting a row drives
    /// <see cref="Selection.activeGameObject"/> so both of those, and the transform gizmo, land on
    /// the same object. What it adds is the mapping the Hierarchy cannot show — which abstract slot
    /// a given scene object is currently cast as.
    /// </remarks>
    internal sealed class CutsceneCastPanel : VisualElement
    {
        private enum BindingState
        {
            Unbound,
            Bound,
            Broken
        }

        private readonly VisualElement rowsContainer = new VisualElement();
        private readonly Label stageStatusLabel = new Label();
        private readonly Button syncToStageButton;

        /// <summary>Raised with the slot index whose prefab should be instantiated and bound.</summary>
        public event Action<int> PlaceRequested;

        /// <summary>Raised with a slot index and the GameObject to bind to it, or null to unbind.</summary>
        public event Action<int, GameObject> BindRequested;

        /// <summary>Raised with the slot index whose row was clicked.</summary>
        public event Action<int> SlotSelected;

        /// <summary>Raised with the slot index whose bound object should be framed in the Scene view.</summary>
        public event Action<int> FrameRequested;

        /// <summary>Raised when the author presses Sync to Stage (amendment A61-T3).</summary>
        public event Action SyncToStageRequested;

        public CutsceneCastPanel()
        {
            style.minWidth = 200f;
            style.paddingLeft = 6f;
            style.paddingTop = 6f;
            style.paddingRight = 4f;

            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4f;

            Label heading = new Label("Cast");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerRow.Add(heading);

            stageStatusLabel.style.flexGrow = 1f;
            stageStatusLabel.style.marginLeft = 6f;
            stageStatusLabel.style.color = new Color(0.68f, 0.68f, 0.72f);
            headerRow.Add(stageStatusLabel);

            syncToStageButton = new Button(() => SyncToStageRequested?.Invoke()) { text = "Sync to Stage" };
            syncToStageButton.tooltip =
                "Writes every bound slot into this scene's CutsceneStageAuthoring component, baking "
                + "one CutsceneStage entity that plays this cutscene at runtime (amendment A61). "
                + "Explicit, never automatic — press it after the cast is the way you want it.";
            headerRow.Add(syncToStageButton);

            Add(headerRow);

            ScrollView rowsScroll = new ScrollView(ScrollViewMode.Vertical);
            rowsScroll.style.flexGrow = 1f;
            rowsScroll.Add(rowsContainer);
            Add(rowsScroll);
        }

        /// <summary>Sets the Stage status text (A61-D2: sync is explicit, so this only ever reports state — it never triggers a write).</summary>
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
                rowsContainer.Add(new Label("No slots yet — add an Actor or Prop slot.")
                { style = { whiteSpace = WhiteSpace.Normal } });
                return;
            }

            bool sceneMatches = !string.IsNullOrEmpty(cutscene.sceneGuid)
                && currentSceneGuid == cutscene.sceneGuid;
            if (!sceneMatches)
            {
                rowsContainer.Add(new Label(
                    "Open the remembered scene to place or bind the cast. Timing edits still work.")
                { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 6f } });
            }

            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
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
            row.EnableInClassList("cutscene-editor__cast-row--selected", isSelected);
            row.RegisterCallback<PointerDownEvent>(_ => SlotSelected?.Invoke(capturedIndex));

            GameObject boundObject;
            BindingState state = ResolveBindingState(cutscene, currentSceneGuid, slot, out boundObject);

            VisualElement titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            Label stateDot = new Label(StateGlyph(state));
            stateDot.style.color = StateColor(state);
            stateDot.style.width = 16f;
            stateDot.tooltip = StateTooltip(state);
            titleRow.Add(stateDot);

            Label nameLabel = new Label(slot.name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleRow.Add(nameLabel);

            Label kindLabel = new Label("  (" + slot.kind + ")");
            kindLabel.style.color = new Color(0.68f, 0.68f, 0.72f);
            titleRow.Add(kindLabel);
            row.Add(titleRow);

            ObjectField bindField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = boundObject
            };
            bindField.SetEnabled(sceneMatches);
            bindField.RegisterValueChangedCallback(
                changeEvent => BindRequested?.Invoke(capturedIndex, changeEvent.newValue as GameObject));
            row.Add(bindField);

            VisualElement actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.marginTop = 2f;

            Button placeButton = new Button(() => PlaceRequested?.Invoke(capturedIndex)) { text = "Place" };
            placeButton.tooltip = slot.actorPrefab == null
                ? "Assign an Actor Prefab on the slot first — Place instantiates it into the scene "
                    + "and binds it."
                : "Instantiates '" + slot.actorPrefab.name + "' at the Scene view pivot and binds it "
                    + "to this slot.";
            // Placing over a live binding is how a slot silently ends up with two actors in the
            // scene and only one of them animating.
            placeButton.SetEnabled(sceneMatches && slot.actorPrefab != null && boundObject == null);
            actionRow.Add(placeButton);

            Button selectButton = new Button(() => SlotSelected?.Invoke(capturedIndex)) { text = "Select" };
            selectButton.SetEnabled(boundObject != null);
            actionRow.Add(selectButton);

            Button frameButton = new Button(() => FrameRequested?.Invoke(capturedIndex)) { text = "Frame" };
            frameButton.SetEnabled(boundObject != null);
            actionRow.Add(frameButton);

            row.Add(actionRow);
            return row;
        }

        private static BindingState ResolveBindingState(
            CutsceneAsset cutscene, string currentSceneGuid, CutsceneSlot slot, out GameObject boundObject)
        {
            boundObject = null;
            CutsceneSlotBindingEntry entry =
                CutsceneSceneBindingUtility.FindBinding(cutscene, currentSceneGuid, slot.SlotId);
            if (entry == null || string.IsNullOrEmpty(entry.globalObjectId))
            {
                return BindingState.Unbound;
            }
            boundObject = CutsceneSceneBindingUtility.ResolveGameObject(entry.globalObjectId);
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

        private static Color StateColor(BindingState state)
        {
            switch (state)
            {
                case BindingState.Bound:
                    return new Color(0.45f, 0.85f, 0.45f);
                case BindingState.Broken:
                    return new Color(0.95f, 0.55f, 0.3f);
                default:
                    return new Color(0.6f, 0.6f, 0.65f);
            }
        }

        private static string StateTooltip(BindingState state)
        {
            switch (state)
            {
                case BindingState.Bound:
                    return "Bound to a live object in this scene.";
                case BindingState.Broken:
                    return "Bound to an object this scene no longer has — re-bind or place again.";
                default:
                    return "Not bound yet — place a prefab or drag a scene object in.";
            }
        }

        /// <summary>The slot whose bound object is <paramref name="selected"/> or an ancestor of it, or −1.</summary>
        /// <remarks>
        /// Walks up the hierarchy because clicking a character in the Scene view usually selects a
        /// part, not the root the slot is bound to — a row that only lit for an exact hit would look
        /// broken most of the time.
        /// </remarks>
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
