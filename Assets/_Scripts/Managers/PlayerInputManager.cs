using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem; // if you’re using the new Input System

public class PlayerInputManager : MonoBehaviour
{
    private EntityManager entityManager;
    private Entity _inputEntity;
    private ActionMaps currentActionMap;

    private void Awake()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Create a single PlayerInput entity for the whole game
        EntityArchetype archetype = entityManager.CreateArchetype(typeof(PlayerInput));
        _inputEntity = entityManager.CreateEntity(archetype);
        
        entityManager.SetComponentData(_inputEntity, new PlayerInput
        {
            moveInput = float2.zero,
            sneakToggleInput = false,
            
            onAttackInput = false,
            onInteractInput = false,
            onRollInput = false
        });
    }

    // ==== Input System callback methods ====
    public void OnMove(InputAction.CallbackContext context)
    {
        float2 value = float2.zero;
        if (context.performed || context.canceled)
        {
            Vector2 v = context.ReadValue<Vector2>();
            value = new float2(v.x, v.y);
        }

        PlayerInput data = entityManager.GetComponentData<PlayerInput>(_inputEntity);
        data.moveInput = value;
        entityManager.SetComponentData(_inputEntity, data);
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        PlayerInput data = entityManager.GetComponentData<PlayerInput>(_inputEntity);
        data.onAttackInput = true;
        entityManager.SetComponentData(_inputEntity, data);
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;

        PlayerInput data = entityManager.GetComponentData<PlayerInput>(_inputEntity);
        data.onInteractInput = true;
        entityManager.SetComponentData(_inputEntity, data);
    }
    
    public void OnUnitControlToggle(InputAction.CallbackContext context)
    {
        if (!context.performed)
            return;
        
        PlayerInput data = entityManager.GetComponentData<PlayerInput>(_inputEntity);

        if (currentActionMap == ActionMaps.Player)
        {
            currentActionMap = ActionMaps.ControlUnits;
            data.activeActionMap = ActionMaps.ControlUnits;
            CameraManager.Instance.SwitchCamera(CinemachineCameraType.ControlUnits);
        }

        else
        {
            currentActionMap = ActionMaps.Player;
            data.activeActionMap = ActionMaps.Player;
            CameraManager.Instance.SwitchCamera(CinemachineCameraType.Player);
        }
        
        entityManager.SetComponentData(_inputEntity, data);
    }

    // etc. for sneak, roll …
}

public struct PlayerInput : IComponentData {
    public float2 moveInput;
    public ActionMaps activeActionMap;
    public bool sneakToggleInput;
    
    public bool onAttackInput;
    public bool onInteractInput;
    public bool onRollInput;
}