using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

public class PlayerInputManager : MonoBehaviour, IUpdateObserver
{
    #region Fields
    [SerializeField] private PlayerInput playerInput;
    
    private EntityManager entityManager;
    private Entity inputEntity;

    private ActionMaps targetActionMap;
    private bool hasPendingMapSwitch;

    // ActionMaps
    private const string PLAYER_ACTION_MAP_NAME = "Player";
    private const string CONTROL_UNITS_ACTION_MAP_NAME = "ControlUnits";
    #endregion
    
    #region Initialization
    private void Awake()
    {
        // Setup Input
        if (playerInput == null)
        {
            playerInput = GetComponent<PlayerInput>();
        }

        if (playerInput == null)
        {
            Debug.LogError("PlayerInputManager: No PlayerInput component found on this GameObject.");
        }
        
        playerInput.actions.Disable();
        playerInput.SwitchCurrentActionMap(PLAYER_ACTION_MAP_NAME);


        // Setup Entities
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        EntityArchetype archetype = entityManager.CreateArchetype(typeof(PlayerInputData));
        inputEntity = entityManager.CreateEntity(archetype);
        
        PlayerInputData initialData = new PlayerInputData
        {
            moveInput = float2.zero,
            lookInput = float2.zero,
            activeActionMap = ActionMaps.Player,
            sneakToggle = false,
            onAttackInput = false,
            onInteractInput = false,
            onRollInput = false
        };
        entityManager.SetComponentData(inputEntity, initialData);
        
        hasPendingMapSwitch = false;
        
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);
    #endregion
    
    public void ObservedUpdate()
    {
        if (!hasPendingMapSwitch)
        {
            return;
        }
        
        ApplyActionMapSwitch(targetActionMap);
        hasPendingMapSwitch = false;
    }

    private void ApplyActionMapSwitch(ActionMaps newMap)
    {
        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);

        // Always clear transient input on mode change
        data.moveInput = float2.zero;
        data.lookInput = float2.zero;

        if (newMap == ActionMaps.Player)
        {
            data.activeActionMap = ActionMaps.Player;

            if (playerInput != null)
            {
                playerInput.SwitchCurrentActionMap(PLAYER_ACTION_MAP_NAME);
            }

            CameraManager.Instance.SwitchCamera(CinemachineCameraType.Player);
        }
        else if (newMap == ActionMaps.ControlUnits)
        {
            data.activeActionMap = ActionMaps.ControlUnits;

            if (playerInput != null)
            {
                playerInput.SwitchCurrentActionMap(CONTROL_UNITS_ACTION_MAP_NAME);
            }

            CameraManager.Instance.SwitchCamera(CinemachineCameraType.ControlUnits);
        }

        entityManager.SetComponentData(inputEntity, data);
    }

    #region Input System callback methods
    
    public void OnMove(InputAction.CallbackContext context)
    {
        float2 value = float2.zero;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = new float2(v.x, v.y);
        }

        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        data.moveInput = value;
        entityManager.SetComponentData(inputEntity, data);
    }
    
    public void OnLook(InputAction.CallbackContext context)
    {
        float2 value = float2.zero;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = new float2(v.x, v.y);
        }

        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        data.lookInput = value;
        entityManager.SetComponentData(inputEntity, data);
    }
    
    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        data.onAttackInput = true;
        entityManager.SetComponentData(inputEntity, data);
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        data.onInteractInput = true;
        entityManager.SetComponentData(inputEntity, data);
    }
    
    public void OnEquipt3(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        data.onEquipt3 = true;
        entityManager.SetComponentData(inputEntity, data);
    }
    
    public void OnCursorMovement(InputAction.CallbackContext context)
    {
        float2 value = float2.zero;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = new float2(v.x, v.y);
        }

        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        data.cursorInput = value;
        entityManager.SetComponentData(inputEntity, data);
    }
    
    public void OnZoom(InputAction.CallbackContext context)
    {
        float value = 0f;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = v.y;
        }

        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);
        data.zoomInput = value;
        entityManager.SetComponentData(inputEntity, data);
    }
    
    public void OnSwitchControls(InputAction.CallbackContext context)
    {
        if (!context.performed)
        {
            return;
        }

        // Read the *real* current mode from ECS, not a local field
        PlayerInputData data = entityManager.GetComponentData<PlayerInputData>(inputEntity);

        if (data.activeActionMap == ActionMaps.Player)
        {
            targetActionMap = ActionMaps.ControlUnits;
        }
        else
        {
            targetActionMap = ActionMaps.Player;
        }

        hasPendingMapSwitch = true;
    }
    // etc. for sneak, roll …
    #endregion
}


