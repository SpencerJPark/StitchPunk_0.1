using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.InputSystem;

public class PlayerInputManager : MonoBehaviour, IUpdateObserver
{
    #region Fields
    [SerializeField] private PlayerInput playerInput;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private Entity playerEntity;
    private bool playerEntityResolved;

    private ActionMaps targetActionMap;
    private bool hasPendingMapSwitch;

    private const string PLAYER_ACTION_MAP_NAME = "Player";
    private const string CONTROL_UNITS_ACTION_MAP_NAME = "ControlUnits";
    private const float ROLL_DURATION = 0.5f;
    #endregion

    #region Initialization
    private void Awake()
    {
        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();

        if (playerInput == null)
            Debug.LogError("PlayerInputManager: No PlayerInput component found on this GameObject.");

        playerInput.actions.Disable();
        playerInput.SwitchCurrentActionMap(PLAYER_ACTION_MAP_NAME);

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());

        hasPendingMapSwitch = false;
        playerEntityResolved = false;
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);
    #endregion

    private bool TryResolvePlayerEntity()
    {
        if (playerEntityResolved) return true;
        if (playerQuery.IsEmpty) return false;
        playerEntity = playerQuery.GetSingletonEntity();
        playerEntityResolved = true;
        return true;
    }

    public void ObservedUpdate()
    {
        if (hasPendingMapSwitch && TryResolvePlayerEntity())
        {
            ApplyActionMapSwitch(targetActionMap);
            hasPendingMapSwitch = false;
        }

        UpdateMouseAimDirection();
    }

    // When aiming with a mouse, compute the world-space direction from the player to the
    // cursor and write it into LookPlayerInput so PlayerAimSystem can use it identically
    // to the gamepad right-stick path.
    private void UpdateMouseAimDirection()
    {
        if (!TryResolvePlayerEntity()) return;
        if (!entityManager.IsComponentEnabled<AimPlayerInput>(playerEntity)) return;
        if (playerInput.currentControlScheme != "Keyboard&Mouse") return;
        if (MouseWorldPosition.Instance == null) return;

        LocalTransform playerTransform = entityManager.GetComponentData<LocalTransform>(playerEntity);
        Vector3 mouseWorld = MouseWorldPosition.Instance.GetPosition();

        float dx = mouseWorld.x - playerTransform.Position.x;
        float dz = mouseWorld.z - playerTransform.Position.z;
        float2 dir = new float2(dx, dz);

        if (math.lengthsq(dir) > 0.01f)
        {
            dir = math.normalize(dir);
            entityManager.SetComponentData(playerEntity, new LookPlayerInput { lookInput = dir });
            entityManager.SetComponentEnabled<LookPlayerInput>(playerEntity, true);
        }
    }

    private void ApplyActionMapSwitch(ActionMaps newMap)
    {
        // Clear continuous inputs on mode change
        entityManager.SetComponentEnabled<MovePlayerInput>(playerEntity, false);
        entityManager.SetComponentEnabled<LookPlayerInput>(playerEntity, false);
        entityManager.SetComponentEnabled<CursorPlayerInput>(playerEntity, false);
        entityManager.SetComponentEnabled<ZoomPlayerInput>(playerEntity, false);
        entityManager.SetComponentEnabled<OnCycleGroupInput>(playerEntity, false);
        entityManager.SetComponentEnabled<OnQuickSelectGroupInput>(playerEntity, false);

        PlayerActionMap actionMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity);
        actionMap.activeActionMap = newMap;
        entityManager.SetComponentData(playerEntity, actionMap);

        if (newMap == ActionMaps.Player)
        {
            playerInput?.SwitchCurrentActionMap(PLAYER_ACTION_MAP_NAME);
            CameraManager.Instance.SwitchCamera(CinemachineCameraType.Player);
        }
        else if (newMap == ActionMaps.ControlUnits)
        {
            playerInput?.SwitchCurrentActionMap(CONTROL_UNITS_ACTION_MAP_NAME);
            CameraManager.Instance.SwitchCamera(CinemachineCameraType.ControlUnits);
        }
    }

    #region Input System callback methods

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float2 value = float2.zero;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = new float2(v.x, v.y);
        }

        entityManager.SetComponentData(playerEntity, new MovePlayerInput { moveInput = value });
        entityManager.SetComponentEnabled<MovePlayerInput>(playerEntity, math.lengthsq(value) > 0f);
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float2 value = float2.zero;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = new float2(v.x, v.y);
        }

        entityManager.SetComponentData(playerEntity, new LookPlayerInput { lookInput = value });
        entityManager.SetComponentEnabled<LookPlayerInput>(playerEntity, math.lengthsq(value) > 0f);
    }

    public void OnCursorMovement(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float2 value = float2.zero;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = new float2(v.x, v.y);
        }

        entityManager.SetComponentData(playerEntity, new CursorPlayerInput { cursorInput = value });
        entityManager.SetComponentEnabled<CursorPlayerInput>(playerEntity, math.lengthsq(value) > 0f);
    }

    public void OnZoom(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float value = 0f;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = v.y;
        }

        entityManager.SetComponentData(playerEntity, new ZoomPlayerInput { zoomInput = value });
        entityManager.SetComponentEnabled<ZoomPlayerInput>(playerEntity, value != 0f);
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentEnabled<OnAttackPlayerInput>(playerEntity, true);
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.started || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentEnabled<OnInteractPlayerInput>(playerEntity, true);
    }

    public void OnRoll(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnRollPlayerInput { rollTime = ROLL_DURATION });
        entityManager.SetComponentEnabled<OnRollPlayerInput>(playerEntity, true);
    }

    public void OnSneak(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentEnabled<OnSneakPlayerInput>(playerEntity, true);
    }

    public void OnDrop(InputAction.CallbackContext context)
    {
        if (!context.started || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentEnabled<OnDropPlayerInput>(playerEntity, true);
    }

    public void OnAim(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;
        float value = context.ReadValue<float>();
        entityManager.SetComponentData(playerEntity, new AimPlayerInput { aimValue = value });
        entityManager.SetComponentEnabled<AimPlayerInput>(playerEntity, value > 0.1f);
    }

    public void OnSwitchControls(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;

        PlayerActionMap actionMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity);
        targetActionMap = actionMap.activeActionMap == ActionMaps.Player
            ? ActionMaps.ControlUnits
            : ActionMaps.Player;

        hasPendingMapSwitch = true;
    }

    public void OnItemSlot1(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        
        entityManager.SetComponentData(playerEntity, new OnEquipmentSlotPlayerInput { slot = 1 });
        entityManager.SetComponentEnabled<OnEquipmentSlotPlayerInput>(playerEntity, true);
    }
    
    public void OnItemSlot2(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        
        entityManager.SetComponentData(playerEntity, new OnEquipmentSlotPlayerInput { slot = 2 });
        entityManager.SetComponentEnabled<OnEquipmentSlotPlayerInput>(playerEntity, true);
    }
    
    public void OnItemSlot3(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        
        entityManager.SetComponentData(playerEntity, new OnEquipmentSlotPlayerInput { slot = 3 });
        entityManager.SetComponentEnabled<OnEquipmentSlotPlayerInput>(playerEntity, true);
    }
    
    public void OnItemSlot4(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;

        entityManager.SetComponentData(playerEntity, new OnEquipmentSlotPlayerInput { slot = 4 });
        entityManager.SetComponentEnabled<OnEquipmentSlotPlayerInput>(playerEntity, true);
    }

    public void OnCycleGroupUp(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnCycleGroupInput { delta = 1 });
        entityManager.SetComponentEnabled<OnCycleGroupInput>(playerEntity, true);
    }

    public void OnCycleGroupDown(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnCycleGroupInput { delta = -1 });
        entityManager.SetComponentEnabled<OnCycleGroupInput>(playerEntity, true);
    }

    public void OnSelectGroup1(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnQuickSelectGroupInput { groupIndex = 1 });
        entityManager.SetComponentEnabled<OnQuickSelectGroupInput>(playerEntity, true);
    }

    public void OnSelectGroup2(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnQuickSelectGroupInput { groupIndex = 2 });
        entityManager.SetComponentEnabled<OnQuickSelectGroupInput>(playerEntity, true);
    }

    public void OnSelectGroup3(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnQuickSelectGroupInput { groupIndex = 3 });
        entityManager.SetComponentEnabled<OnQuickSelectGroupInput>(playerEntity, true);
    }

    public void OnSelectGroup4(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnQuickSelectGroupInput { groupIndex = 4 });
        entityManager.SetComponentEnabled<OnQuickSelectGroupInput>(playerEntity, true);
    }

    #endregion
}
