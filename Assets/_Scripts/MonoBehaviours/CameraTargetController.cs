using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class CameraTargetController : MonoBehaviour, IUpdateObserver
{
    private EntityManager _entityManager;

    private EntityQuery _playerQuery;
    private Entity _playerEntity;
    private bool _hasPlayer;

    private EntityQuery _inputQuery;
    private Entity _inputEntity;
    private bool _hasInput;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Player entity (position/rotation comes from here)
        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerCharacter>(),
            ComponentType.ReadOnly<LocalTransform>());

        // Player input singleton (activeActionMap, moveInput, etc.)
        _inputQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerInput>());
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        // --- Bind player entity lazily ---
        if (!_hasPlayer)
        {
            if (_playerQuery.IsEmpty)
                return; // no player yet

            _playerEntity = _playerQuery.GetSingletonEntity();
            _hasPlayer = true;
        }

        if (!_entityManager.Exists(_playerEntity))
        {
            _hasPlayer = false;
            return;
        }

        // --- Bind input entity lazily ---
        if (!_hasInput)
        {
            if (_inputQuery.IsEmpty)
                return; // no input singleton yet

            _inputEntity = _inputQuery.GetSingletonEntity();
            _hasInput = true;
        }

        if (!_entityManager.Exists(_inputEntity))
        {
            _hasInput = false;
            return;
        }

        // Get current input state (including active action map)
        PlayerInput playerInput = _entityManager.GetComponentData<PlayerInput>(_inputEntity);

        // Branch camera behavior by current action map
        switch (playerInput.activeActionMap)
        {
            case ActionMaps.Player:
            case ActionMaps.Vehicle:
                PlayerFollowMove();
                break;

            case ActionMaps.ControlUnits:
                FreeMovement(playerInput);
                break;

            case ActionMaps.MapUI:
                // maybe free movement or special behaviour
                CenterOnPlayer();
                break;
        }
    }

    private void PlayerFollowMove()
    {
        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);

        // You might want smooth follow here, but for now: direct match
        transform.position = playerTransform.Position;
        transform.rotation = playerTransform.Rotation;
    }

    private void FreeMovement(PlayerInput input)
    {
        float moveSpeed = 10f;
        Vector3 move = new Vector3(input.moveInput.x, 0f, input.moveInput.y);

        transform.position += move * moveSpeed * Time.deltaTime;
    }

    private void CenterOnPlayer()
    {
        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);

        // Snap or lerp back to the player
        transform.position = playerTransform.Position;
        // rotation optional for map mode – maybe fixed top-down instead
    }
}
