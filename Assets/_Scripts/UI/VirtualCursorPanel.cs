using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

// The virtual cursor is not the OS pointer, so UI Toolkit never sees it: this picks buttons under it, tints the hovered one
// and runs the clicked one, the same way WorkstationMenuUI does. One instance per panel; the router lets several coexist.
public sealed class VirtualCursorPanel
{
    private static readonly Color HoverColor = new Color(0.45f, 0.45f, 0.5f, 1f);

    private readonly VisualElement panelRoot;
    private readonly Func<bool> isShown;
    private readonly Dictionary<Button, Action> buttonActions = new Dictionary<Button, Action>();
    private Button hoveredButton;

    public VirtualCursorPanel(VisualElement panelRoot, Func<bool> isShown)
    {
        this.panelRoot = panelRoot;
        this.isShown = isShown;
    }

    public void Register()
    {
        VirtualCursorUiRouter.Register(IsScreenPositionOverPanel, TryClickAtScreenPosition);
    }

    public void Unregister()
    {
        VirtualCursorUiRouter.Unregister(IsScreenPositionOverPanel, TryClickAtScreenPosition);
        SetHoveredButton(null);
        buttonActions.Clear();
    }

    // Wires both the OS pointer (clicked) and the virtual cursor (the dictionary).
    public void Track(Button button, Action handler)
    {
        if (button == null)
            return;
        button.clicked += handler;
        MapForVirtualCursor(button, handler);
    }

    // For a long-lived button whose OS-pointer handler is already attached: re-add it after ClearTrackedButtons.
    public void MapForVirtualCursor(Button button, Action handler)
    {
        if (button != null)
            buttonActions[button] = handler;
    }

    public void ClearTrackedButtons()
    {
        SetHoveredButton(null);
        buttonActions.Clear();
    }

    public void UpdateHover(EntityManager entityManager, Entity playerEntity)
    {
        Button buttonUnderCursor = null;
        if (playerEntity != Entity.Null && entityManager.HasComponent<CursorScreenPosition>(playerEntity))
        {
            float2 cursorPosition = entityManager.GetComponentData<CursorScreenPosition>(playerEntity).Value;
            buttonUnderCursor = FindAncestorOrSelfButton(Pick(new Vector2(cursorPosition.x, cursorPosition.y)));
        }
        SetHoveredButton(buttonUnderCursor);
    }

    public bool IsScreenPositionOverPanel(Vector2 screenPositionBottomLeft)
    {
        return Pick(screenPositionBottomLeft) != null;
    }

    // True when the click landed on the panel at all, so it never also commands minions.
    public bool TryClickAtScreenPosition(Vector2 screenPositionBottomLeft)
    {
        VisualElement picked = Pick(screenPositionBottomLeft);
        if (picked == null)
            return false;

        Button button = FindAncestorOrSelfButton(picked);
        if (button != null && button.enabledInHierarchy && buttonActions.TryGetValue(button, out Action action))
            action();
        return true;
    }

    private VisualElement Pick(Vector2 screenPositionBottomLeft)
    {
        if (panelRoot == null || panelRoot.panel == null || !isShown())
            return null;

        // UI Toolkit screen space has Y down, Input.mousePosition-style space has Y up.
        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
            panelRoot.panel, new Vector2(screenPositionBottomLeft.x, Screen.height - screenPositionBottomLeft.y));
        VisualElement picked = panelRoot.panel.Pick(panelPosition);
        for (VisualElement current = picked; current != null; current = current.parent)
        {
            if (current == panelRoot)
                return picked;
        }
        return null;
    }

    private static Button FindAncestorOrSelfButton(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current is Button button)
                return button;
        }
        return null;
    }

    private void SetHoveredButton(Button button)
    {
        if (button == hoveredButton)
            return;
        if (hoveredButton != null)
            hoveredButton.style.backgroundColor = StyleKeyword.Null;
        hoveredButton = button;
        if (hoveredButton != null)
            hoveredButton.style.backgroundColor = HoverColor;
    }
}
