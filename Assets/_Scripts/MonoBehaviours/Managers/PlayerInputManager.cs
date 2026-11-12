using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

public class PlayerInputManager : MonoBehaviour, IUpdateObserver
{
    public static PlayerInputManager Instance { get; private set; }

    #region Fields
    [SerializeField] private PlayerInput playerInput;
    [Tooltip("Off = Tab toggles Control Units anywhere (debug). On = mount a workstation to command units (CampLoop_System.md CL-A4); Tab on foot cycles the active squad.")]
    [SerializeField] private bool requireWorkstationForUnitControl = true;

    private EntityManager entityManager;
    private EntityQuery playerQuery;
    private Entity playerEntity;
    private bool playerEntityResolved;

    private ActionMaps targetActionMap;
    private bool hasPendingMapSwitch;

    private bool lastKnownMountedState;
    private InputAction deployWorkstationAction;

    // The Vehicle map's actions are polled, like DeployWorkstation, so driving needs no scene event wiring.
    private bool lastKnownDrivingState;
    private InputAction vehicleSteerAction;
    private InputAction exitVehicleAction;

    // Trade letter open (Player map) and close (UI map) are polled by TradeLetterUI / TradeOrdersHudUI as rebindable actions.
    private InputAction openLetterAction;
    private InputAction closeLetterAction;

    private bool menuOpen;
    private ActionMaps mapBeforeMenu;

    // A panel that needs the real pointer (the trade letter) borrows the pause menu's mode: UI map, free cursor, world frozen.
    // Esc stays the pause-menu toggle: the menu opens over the panel and closing it returns to the panel.
    private bool modalUiOpen;
    private ActionMaps mapBeforeModalUi;
    private float timeScaleBeforeModalUi = 1f;
    private bool cursorVisibleBeforeModalUi;
    private CursorLockMode cursorLockBeforeModalUi;

    // Polled (like DeployWorkstation) so opening the menu needs no PlayerInput event wiring in the scene.
    private InputAction[] toggleMenuActions;

    private const string PLAYER_ACTION_MAP_NAME = "Player";
    private const string CONTROL_UNITS_ACTION_MAP_NAME = "ControlUnits";
    private const string UI_ACTION_MAP_NAME = "UI";
    private const string VEHICLE_ACTION_MAP_NAME = "Vehicle";
    #endregion

    #region Initialization
    private void Awake()
    {
        Instance = this;

        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();

        if (playerInput == null)
            Debug.LogError("PlayerInputManager: No PlayerInput component found on this GameObject.");

        InputRebinding.Register(playerInput.actions);
        deployWorkstationAction = playerInput.actions.FindAction("DeployWorkstation");
        toggleMenuActions = new InputAction[]
        {
            playerInput.actions.FindAction(PLAYER_ACTION_MAP_NAME + "/ToggleMenu"),
            playerInput.actions.FindAction(CONTROL_UNITS_ACTION_MAP_NAME + "/ToggleMenu"),
            playerInput.actions.FindAction(VEHICLE_ACTION_MAP_NAME + "/ToggleMenu"),
            playerInput.actions.FindAction(UI_ACTION_MAP_NAME + "/Cancel"),
        };
        ConfigureUiInputModule();
        vehicleSteerAction = playerInput.actions.FindAction(VEHICLE_ACTION_MAP_NAME + "/Steer");
        exitVehicleAction = playerInput.actions.FindAction(VEHICLE_ACTION_MAP_NAME + "/ExitVehicle");
        openLetterAction = playerInput.actions.FindAction(PLAYER_ACTION_MAP_NAME + "/OpenLetter");
        closeLetterAction = playerInput.actions.FindAction(UI_ACTION_MAP_NAME + "/CloseLetter");
        playerInput.actions.Disable();
        playerInput.SwitchCurrentActionMap(PLAYER_ACTION_MAP_NAME);

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());

        hasPendingMapSwitch = false;
        playerEntityResolved = false;
        menuOpen = false;
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        // Never leave the game frozen if this is torn down while a modal panel is up.
        if (modalUiOpen)
            Time.timeScale = timeScaleBeforeModalUi;
    }
    #endregion

    // An EventSystem without an input module turns UI Toolkit's fallback input off; this module is fed by the UI map so UI input flows only while it is current.
    private void ConfigureUiInputModule()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null) return;

        InputSystemUIInputModule uiModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (uiModule == null)
            uiModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

        uiModule.actionsAsset = playerInput.actions;
        uiModule.point = UiActionReference("Point");
        uiModule.leftClick = UiActionReference("Click");
        uiModule.rightClick = UiActionReference("RightClick");
        uiModule.middleClick = UiActionReference("MiddleClick");
        uiModule.scrollWheel = UiActionReference("ScrollWheel");
        uiModule.move = UiActionReference("Navigate");
        uiModule.submit = UiActionReference("Submit");
        uiModule.cancel = UiActionReference("Cancel");
        uiModule.trackedDevicePosition = UiActionReference("TrackedDevicePosition");
        uiModule.trackedDeviceOrientation = UiActionReference("TrackedDeviceOrientation");
    }

    private InputActionReference UiActionReference(string actionName)
    {
        InputAction action = playerInput.actions.FindAction(UI_ACTION_MAP_NAME + "/" + actionName);
        return action != null ? InputActionReference.Create(action) : null;
    }

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

        if (TryResolvePlayerEntity())
        {
            WatchWorkstationMount();
            WatchCaravanDriving();
            PollDeployWorkstationKey();
            PollVehicleControls();
        }

        UpdateMouseAimDirection();
        PollToggleMenuKeys();
    }

    private void PollToggleMenuKeys()
    {
        for (int actionIndex = 0; actionIndex < toggleMenuActions.Length; actionIndex++)
        {
            InputAction toggleAction = toggleMenuActions[actionIndex];
            if (toggleAction != null && toggleAction.WasPressedThisFrame())
            {
                ToggleMenu();
                return;
            }
        }
    }

    private void WatchWorkstationMount()
    {
        if (!requireWorkstationForUnitControl) return;
        if (!entityManager.HasComponent<WorkstationMounted>(playerEntity)) return;

        bool mounted = entityManager.IsComponentEnabled<WorkstationMounted>(playerEntity);
        if (mounted == lastKnownMountedState) return;
        lastKnownMountedState = mounted;

        RouteActionMapChange(mounted ? ActionMaps.ControlUnits : ActionMaps.Player);
    }

    // Mirrors WatchWorkstationMount: ECS decides when the player is driving (board, park, wreck), this follows.
    private void WatchCaravanDriving()
    {
        if (!entityManager.HasComponent<DrivingCaravan>(playerEntity)) return;

        bool driving = entityManager.IsComponentEnabled<DrivingCaravan>(playerEntity);
        if (driving == lastKnownDrivingState) return;
        lastKnownDrivingState = driving;

        RouteActionMapChange(driving ? ActionMaps.Vehicle : ActionMaps.Player);
    }

    private void RouteActionMapChange(ActionMaps newMap)
    {
        if (modalUiOpen)
            mapBeforeModalUi = newMap;
        else if (menuOpen)
            mapBeforeMenu = newMap;
        else
            ApplyActionMapSwitch(newMap);
    }

    private void PollVehicleControls()
    {
        if (!entityManager.HasComponent<CaravanDriveInput>(playerEntity)) return;
        if (entityManager.GetComponentData<PlayerActionMap>(playerEntity).activeActionMap != ActionMaps.Vehicle) return;

        Vector2 steer = vehicleSteerAction != null ? vehicleSteerAction.ReadValue<Vector2>() : Vector2.zero;
        entityManager.SetComponentData(playerEntity, new CaravanDriveInput { steer = new float2(steer.x, steer.y) });

        if (exitVehicleAction != null && exitVehicleAction.WasPressedThisFrame()
            && entityManager.HasComponent<OnExitVehicleInput>(playerEntity))
            entityManager.SetComponentEnabled<OnExitVehicleInput>(playerEntity, true);
    }

    // Polled because the scene's PlayerInput uses Unity Events and a new action would need scene wiring.
    private void PollDeployWorkstationKey()
    {
        if (deployWorkstationAction == null || !deployWorkstationAction.WasPressedThisFrame()) return;
        if (!entityManager.HasComponent<PlayerActionMap>(playerEntity)) return;
        if (entityManager.GetComponentData<PlayerActionMap>(playerEntity).activeActionMap != ActionMaps.Player) return;
        if (!entityManager.HasComponent<OnDeployWorkstationInput>(playerEntity)) return;

        entityManager.SetComponentEnabled<OnDeployWorkstationInput>(playerEntity, true);
    }

    // Writes the player-to-cursor direction into LookPlayerInput so PlayerAimSystem treats it like the right stick.
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
        entityManager.SetComponentEnabled<OnQuickCommandInput>(playerEntity, false);
        entityManager.SetComponentEnabled<OnSelectPlayerInput>(playerEntity, false);
        entityManager.SetComponentEnabled<OnCommandPlayerInput>(playerEntity, false);
        entityManager.SetComponentEnabled<OnSnapCameraBackInput>(playerEntity, false);

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
        else if (newMap == ActionMaps.Vehicle)
        {
            playerInput?.SwitchCurrentActionMap(VEHICLE_ACTION_MAP_NAME);
            // The game keeps one fixed camera angle (Design_Decisions: static rotation 3rd person); a scene
            // without a vehicle vcam just keeps following the seated player with the player camera.
            CameraManager.Instance.SwitchCamera(CameraManager.Instance.HasCamera(CinemachineCameraType.Vehicle)
                ? CinemachineCameraType.Vehicle
                : CinemachineCameraType.Player);
        }
        else if (newMap == ActionMaps.Menu)
        {
            // Switch to the UI map: disables gameplay input while the menu is up. Camera unchanged.
            playerInput?.SwitchCurrentActionMap(UI_ACTION_MAP_NAME);
        }
    }

    #region Menu

    // Bound (via PlayerInput Unity Events) to BOTH the gameplay "ToggleMenu" action and the
    // "UI/Cancel" action, so the same key opens (from gameplay) and closes (from the menu).
    public void OnToggleMenu(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        ToggleMenu();
    }

    private void ToggleMenu()
    {
        // Escape cancels a rebind and is also this action's key; that press must not close the menu under the player.
        if (InputRebinding.IsRebinding || Time.frameCount - InputRebinding.LastRebindEndFrame <= 1) return;

        if (menuOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    private void OpenMenu()
    {
        if (menuOpen || !TryResolvePlayerEntity()) return;

        PlayerActionMap actionMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity);
        mapBeforeMenu = actionMap.activeActionMap;

        menuOpen = true;
        ApplyActionMapSwitch(ActionMaps.Menu);
        GameMenuManager.Instance?.Show();
    }

    // Public so the menu's Close button can call it through PlayerInputManager.Instance.
    public void CloseMenu()
    {
        if (!menuOpen || !TryResolvePlayerEntity()) return;

        menuOpen = false;
        ApplyActionMapSwitch(mapBeforeMenu);
        GameMenuManager.Instance?.Hide();

        // Hide() unpauses; a panel that was open underneath is still waiting to be read.
        if (modalUiOpen)
            Time.timeScale = 0f;
    }

    public bool IsModalUiOpen => modalUiOpen;

    public bool OpenLetterPressedThisFrame() => openLetterAction != null && openLetterAction.WasPressedThisFrame();

    public bool CloseLetterPressedThisFrame() => closeLetterAction != null && closeLetterAction.WasPressedThisFrame();

    public void OpenModalUi()
    {
        if (modalUiOpen || menuOpen || !TryResolvePlayerEntity()) return;

        PlayerActionMap actionMap = entityManager.GetComponentData<PlayerActionMap>(playerEntity);
        mapBeforeModalUi = actionMap.activeActionMap;

        modalUiOpen = true;
        timeScaleBeforeModalUi = Time.timeScale;
        cursorVisibleBeforeModalUi = Cursor.visible;
        cursorLockBeforeModalUi = Cursor.lockState;
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        ApplyActionMapSwitch(ActionMaps.Menu);
    }

    public void CloseModalUi()
    {
        if (!modalUiOpen) return;

        modalUiOpen = false;

        // The pause menu is up over the panel: it restores the map and time itself when it closes.
        if (menuOpen)
        {
            mapBeforeMenu = mapBeforeModalUi;
            return;
        }

        ApplyActionMapSwitch(mapBeforeModalUi);
        Time.timeScale = timeScaleBeforeModalUi;
        Cursor.visible = cursorVisibleBeforeModalUi;
        Cursor.lockState = cursorLockBeforeModalUi;
    }

    #endregion

    #region PlayerControl

    private void EnablePlayerComponentWhenPerformed<TComponent>(InputAction.CallbackContext context)
        where TComponent : unmanaged, IComponentData, IEnableableComponent
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentEnabled<TComponent>(playerEntity, true);
    }

    private void EnablePlayerComponentWhenStarted<TComponent>(InputAction.CallbackContext context)
        where TComponent : unmanaged, IComponentData, IEnableableComponent
    {
        if (!context.started || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentEnabled<TComponent>(playerEntity, true);
    }

    private static float2 ReadVector2OrZero(InputAction.CallbackContext context)
    {
        if (!(context.performed || context.canceled)) return float2.zero;
        Vector2 rawValue = context.ReadValue<Vector2>();
        return new float2(rawValue.x, rawValue.y);
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float2 value = ReadVector2OrZero(context);
        entityManager.SetComponentData(playerEntity, new MovePlayerInput { moveInput = value });
        entityManager.SetComponentEnabled<MovePlayerInput>(playerEntity, math.lengthsq(value) > 0f);
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float2 value = ReadVector2OrZero(context);
        entityManager.SetComponentData(playerEntity, new LookPlayerInput { lookInput = value });
        entityManager.SetComponentEnabled<LookPlayerInput>(playerEntity, math.lengthsq(value) > 0f);
    }


    public void OnAttack(InputAction.CallbackContext context)
        => EnablePlayerComponentWhenPerformed<OnAttackPlayerInput>(context);

    public void OnInteract(InputAction.CallbackContext context)
        => EnablePlayerComponentWhenStarted<OnInteractPlayerInput>(context);

    public void OnRoll(InputAction.CallbackContext context)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        if (!entityManager.HasComponent<PlayerRollSettings>(playerEntity)) return;
        float inputBufferWindow = entityManager.GetComponentData<PlayerRollSettings>(playerEntity).inputBufferWindow;
        entityManager.SetComponentData(playerEntity, new OnRollPlayerInput { bufferRemaining = inputBufferWindow });
        entityManager.SetComponentEnabled<OnRollPlayerInput>(playerEntity, true);
    }

    public void OnSneak(InputAction.CallbackContext context)
        => EnablePlayerComponentWhenPerformed<OnSneakPlayerInput>(context);

    public void OnDrop(InputAction.CallbackContext context)
        => EnablePlayerComponentWhenStarted<OnDropPlayerInput>(context);

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

        if (requireWorkstationForUnitControl)
        {
            if (actionMap.activeActionMap == ActionMaps.Player)
            {
                if (entityManager.HasComponent<OnCycleGroupInput>(playerEntity))
                {
                    entityManager.SetComponentData(playerEntity, new OnCycleGroupInput { delta = 1 });
                    entityManager.SetComponentEnabled<OnCycleGroupInput>(playerEntity, true);
                }
            }
            else if (actionMap.activeActionMap == ActionMaps.ControlUnits
                && entityManager.HasComponent<WorkstationDismountRequest>(playerEntity))
                entityManager.SetComponentEnabled<WorkstationDismountRequest>(playerEntity, true);
            return;
        }

        targetActionMap = actionMap.activeActionMap == ActionMaps.Player
            ? ActionMaps.ControlUnits
            : ActionMaps.Player;

        hasPendingMapSwitch = true;
    }

    public void OnItemSlot1(InputAction.CallbackContext context) => EnableEquipmentSlot(context, 1);

    public void OnItemSlot2(InputAction.CallbackContext context) => EnableEquipmentSlot(context, 2);

    public void OnItemSlot3(InputAction.CallbackContext context) => EnableEquipmentSlot(context, 3);

    public void OnItemSlot4(InputAction.CallbackContext context) => EnableEquipmentSlot(context, 4);

    private void EnableEquipmentSlot(InputAction.CallbackContext context, int slotIndex)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;

        entityManager.SetComponentData(playerEntity, new OnEquipmentSlotPlayerInput { slot = slotIndex });
        entityManager.SetComponentEnabled<OnEquipmentSlotPlayerInput>(playerEntity, true);
    }

    #endregion

    #region UnitControl

    public void OnCursorMovement(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float2 value = ReadVector2OrZero(context);
        entityManager.SetComponentData(playerEntity, new CursorPlayerInput { cursorInput = value });
        entityManager.SetComponentEnabled<CursorPlayerInput>(playerEntity, math.lengthsq(value) > 0f);
    }

    public void OnZoom(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;

        float value = 0f;
        if (context.performed || context.canceled)
            value = context.ReadValue<float>();

        entityManager.SetComponentData(playerEntity, new ZoomPlayerInput { zoomInput = value });
        entityManager.SetComponentEnabled<ZoomPlayerInput>(playerEntity, value != 0f);
    }

    public void OnSelect(InputAction.CallbackContext context)
    {
        if (!TryResolvePlayerEntity()) return;
        if (context.started)
        {
            entityManager.SetComponentData(playerEntity, new OnSelectPlayerInput { isReleased = false });
            entityManager.SetComponentEnabled<OnSelectPlayerInput>(playerEntity, true);
        }
        else if (context.canceled)
        {
            entityManager.SetComponentData(playerEntity, new OnSelectPlayerInput { isReleased = true });
            entityManager.SetComponentEnabled<OnSelectPlayerInput>(playerEntity, true);
        }
    }
    
    public void OnCommand(InputAction.CallbackContext context)
        => EnablePlayerComponentWhenPerformed<OnCommandPlayerInput>(context);

    public void OnSnapCameraBack(InputAction.CallbackContext context)
        => EnablePlayerComponentWhenPerformed<OnSnapCameraBackInput>(context);

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

    public void OnQuickCommand1(InputAction.CallbackContext context) => EnableQuickCommand(context, 1);

    public void OnQuickCommand2(InputAction.CallbackContext context) => EnableQuickCommand(context, 2);

    public void OnQuickCommand3(InputAction.CallbackContext context) => EnableQuickCommand(context, 3);

    public void OnQuickCommand4(InputAction.CallbackContext context) => EnableQuickCommand(context, 4);

    private void EnableQuickCommand(InputAction.CallbackContext context, int commandIndex)
    {
        if (!context.performed || !TryResolvePlayerEntity()) return;
        entityManager.SetComponentData(playerEntity, new OnQuickCommandInput { commandIndex = commandIndex });
        entityManager.SetComponentEnabled<OnQuickCommandInput>(playerEntity, true);
    }

    #endregion
}
