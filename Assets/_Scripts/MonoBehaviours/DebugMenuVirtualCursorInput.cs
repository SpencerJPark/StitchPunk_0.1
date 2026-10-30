using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// OnGUI only sees the OS pointer, never the in-game virtual cursor. This registers a debug menu's area with
// VirtualCursorUiRouter, parks a virtual-cursor click, and lets the button under it claim the click during the
// same frame's Repaint pass.
public sealed class DebugMenuVirtualCursorInput
{
    private static readonly Color VirtualCursorHoverTint = new Color(1f, 1f, 1f, 0.25f);

    private readonly Rect menuAreaRect;
    private readonly Func<bool> isMenuShown;
    private readonly EntityManager entityManager;
    private readonly EntityQuery playerCursorQuery;

    private Vector2 pendingVirtualClickGuiScreenPosition;
    private int pendingVirtualClickFrame = -1;
    private bool virtualCursorActive;
    private Vector2 virtualCursorGuiScreenPosition;

    public DebugMenuVirtualCursorInput(EntityManager entityManager, Rect menuAreaRect, Func<bool> isMenuShown)
    {
        this.entityManager = entityManager;
        this.menuAreaRect = menuAreaRect;
        this.isMenuShown = isMenuShown;
        playerCursorQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<PlayerActionMap>(),
            ComponentType.ReadOnly<CursorScreenPosition>());
    }

    public void Register()   => VirtualCursorUiRouter.Register(IsScreenPositionOverMenu, TryClickMenuAtScreenPosition);
    public void Unregister() => VirtualCursorUiRouter.Unregister(IsScreenPositionOverMenu, TryClickMenuAtScreenPosition);

    // Call at the top of OnGUI, before BeginArea.
    public void BeginMenuGui()
    {
        virtualCursorActive = false;
        if (playerCursorQuery.TryGetSingletonEntity<Player>(out Entity playerEntity)
            && entityManager.GetComponentData<PlayerActionMap>(playerEntity).activeActionMap == ActionMaps.ControlUnits)
        {
            float2 cursorScreenPositionBottomLeft = entityManager.GetComponentData<CursorScreenPosition>(playerEntity).Value;
            virtualCursorGuiScreenPosition = ToGuiScreenPosition(new Vector2(cursorScreenPositionBottomLeft.x, cursorScreenPositionBottomLeft.y));
            virtualCursorActive = true;
        }

        // The hidden OS pointer sits somewhere else on screen; its clicks would press the wrong button.
        if (virtualCursorActive && Event.current.isMouse)
            Event.current.Use();
    }

    public bool Button(string label, params GUILayoutOption[] layoutOptions)
    {
        bool clicked = GUILayout.Button(label, layoutOptions);
        if (!virtualCursorActive || Event.current.type != EventType.Repaint)
            return clicked;

        Rect buttonRect = GUILayoutUtility.GetLastRect();
        if (buttonRect.Contains(GUIUtility.ScreenToGUIPoint(virtualCursorGuiScreenPosition)))
            GUI.DrawTexture(buttonRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, VirtualCursorHoverTint, 0f, 0f);

        if (pendingVirtualClickFrame == Time.frameCount
            && buttonRect.Contains(GUIUtility.ScreenToGUIPoint(pendingVirtualClickGuiScreenPosition)))
        {
            pendingVirtualClickFrame = -1;
            return true;
        }
        return clicked;
    }

    private bool IsScreenPositionOverMenu(Vector2 screenPositionBottomLeft)
    {
        return isMenuShown() && menuAreaRect.Contains(ToGuiScreenPosition(screenPositionBottomLeft));
    }

    private bool TryClickMenuAtScreenPosition(Vector2 screenPositionBottomLeft)
    {
        if (!IsScreenPositionOverMenu(screenPositionBottomLeft))
            return false;

        pendingVirtualClickGuiScreenPosition = ToGuiScreenPosition(screenPositionBottomLeft);
        pendingVirtualClickFrame = Time.frameCount;
        return true;
    }

    private static Vector2 ToGuiScreenPosition(Vector2 screenPositionBottomLeft)
    {
        return new Vector2(screenPositionBottomLeft.x, Screen.height - screenPositionBottomLeft.y);
    }
}
