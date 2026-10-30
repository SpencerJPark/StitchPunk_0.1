using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

// Throwaway OnGUI resource cheat menu — delete once the real HUD and producers exist.
public class DebugResourceMenu : MonoBehaviour
{
    private const int CapValue = 20;
    private const int DeltaAmount = 10;

    private static readonly Rect MenuAreaRect = new Rect(390f, 10f, 380f, 280f);
    private static readonly Color VirtualCursorHoverTint = new Color(1f, 1f, 1f, 0.25f);

    private static readonly ResourceType[] DisplayedResourceTypes =
    {
        ResourceType.Wood,
        ResourceType.ScrapMetal,
        ResourceType.Corpse,
        ResourceType.Electricity,
        ResourceType.Currency,
        ResourceType.RevivalRound,
    };

    // Not F1: laptop keyboards send media keys on the F row unless Fn is held.
    [SerializeField] private Key toggleVisibilityKey = Key.Backquote;
    [SerializeField] private bool isMenuVisible = true;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private bool worldReady;

    // OnGUI only sees the OS pointer, so a virtual-cursor click is parked here and claimed by the button
    // it lands on during the same frame's Repaint pass.
    private Vector2 pendingVirtualClickGuiScreenPosition;
    private int pendingVirtualClickFrame = -1;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        entityManager = world.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<ResourceStack>());
        worldReady = true;
    }

    private void OnEnable()  => VirtualCursorUiRouter.Register(IsScreenPositionOverMenu, TryClickMenuAtScreenPosition);
    private void OnDisable() => VirtualCursorUiRouter.Unregister(IsScreenPositionOverMenu, TryClickMenuAtScreenPosition);

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleVisibilityKey].wasPressedThisFrame)
            isMenuVisible = !isMenuVisible;
    }

    private bool IsScreenPositionOverMenu(Vector2 screenPositionBottomLeft)
    {
        return isMenuVisible && worldReady && MenuAreaRect.Contains(ToGuiScreenPosition(screenPositionBottomLeft));
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

    private void OnGUI()
    {
        if (!isMenuVisible || !worldReady || World.DefaultGameObjectInjectionWorld == null)
            return;

        if (!playerQuery.TryGetSingletonEntity<Player>(out Entity playerEntity))
        {
            GUILayout.BeginArea(MenuAreaRect, GUI.skin.box);
            GUILayout.Label("No player entity with a ResourceStack.");
            GUILayout.EndArea();
            return;
        }

        bool virtualCursorActive = IsVirtualCursorActive(playerEntity);
        // The hidden OS pointer sits somewhere else on screen; its clicks would press the wrong button.
        if (virtualCursorActive && Event.current.isMouse)
            Event.current.Use();

        GUILayout.BeginArea(MenuAreaRect, GUI.skin.box);
        GUILayout.Label($"Resources (debug) — press {toggleVisibilityKey} to hide");

        // The caravan holds the ledger once it links.
        Entity ledgerHolder = ResourceLedger.HolderOf(entityManager, playerEntity);
        Vector2 virtualCursorGuiScreenPosition = virtualCursorActive ? ReadVirtualCursorGuiScreenPosition(playerEntity) : Vector2.zero;

        foreach (ResourceType resourceType in DisplayedResourceTypes)
        {
            DynamicBuffer<ResourceStack> resourceStacks = entityManager.GetBuffer<ResourceStack>(ledgerHolder, true);
            int currentAmount = ResourceLedger.Get(in resourceStacks, resourceType);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{resourceType}: {currentAmount}", GUILayout.Width(130f));
            if (DrawButtonClickableByVirtualCursor("+10", virtualCursorActive, virtualCursorGuiScreenPosition))
                QueueChange(ledgerHolder, resourceType, DeltaAmount);
            if (DrawButtonClickableByVirtualCursor("-10", virtualCursorActive, virtualCursorGuiScreenPosition))
                QueueChange(ledgerHolder, resourceType, -DeltaAmount);
            if (DrawButtonClickableByVirtualCursor($"Set cap {CapValue}", virtualCursorActive, virtualCursorGuiScreenPosition))
                SetCap(ledgerHolder, resourceType);
            GUILayout.EndHorizontal();
        }

        if (DrawButtonClickableByVirtualCursor("Clear caps", virtualCursorActive, virtualCursorGuiScreenPosition))
            entityManager.GetBuffer<StorageCapacity>(ledgerHolder).Clear();

        GUILayout.EndArea();
    }

    private bool IsVirtualCursorActive(Entity playerEntity)
    {
        return entityManager.HasComponent<PlayerActionMap>(playerEntity)
            && entityManager.HasComponent<CursorScreenPosition>(playerEntity)
            && entityManager.GetComponentData<PlayerActionMap>(playerEntity).activeActionMap == ActionMaps.ControlUnits;
    }

    private Vector2 ReadVirtualCursorGuiScreenPosition(Entity playerEntity)
    {
        float2 cursorScreenPositionBottomLeft = entityManager.GetComponentData<CursorScreenPosition>(playerEntity).Value;
        return ToGuiScreenPosition(new Vector2(cursorScreenPositionBottomLeft.x, cursorScreenPositionBottomLeft.y));
    }

    private bool DrawButtonClickableByVirtualCursor(string label, bool virtualCursorActive, Vector2 virtualCursorGuiScreenPosition)
    {
        bool clicked = GUILayout.Button(label);
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

    private void QueueChange(Entity ledgerHolder, ResourceType resourceType, int amount)
    {
        entityManager.GetBuffer<ResourceChangeRequest>(ledgerHolder).Add(new ResourceChangeRequest
        {
            resourceType = resourceType,
            changeType   = ResourceChangeType.Add,
            value        = amount,
        });
    }

    private void SetCap(Entity ledgerHolder, ResourceType resourceType)
    {
        DynamicBuffer<StorageCapacity> capacities = entityManager.GetBuffer<StorageCapacity>(ledgerHolder);
        for (int rowIndex = 0; rowIndex < capacities.Length; rowIndex++)
        {
            if (capacities[rowIndex].resourceType != resourceType)
                continue;
            capacities[rowIndex] = new StorageCapacity { resourceType = resourceType, cap = CapValue };
            return;
        }
        capacities.Add(new StorageCapacity { resourceType = resourceType, cap = CapValue });
    }
}
