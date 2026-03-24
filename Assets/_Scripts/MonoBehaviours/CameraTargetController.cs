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

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<PlayerActionMap>());
    }

    private void OnEnable()  => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        if (!_hasPlayer)
        {
            if (_playerQuery.IsEmpty) return;
            _playerEntity = _playerQuery.GetSingletonEntity();
            _hasPlayer = true;
        }

        if (!_entityManager.Exists(_playerEntity))
        {
            _hasPlayer = false;
            return;
        }

        PlayerActionMap actionMap = _entityManager.GetComponentData<PlayerActionMap>(_playerEntity);

        switch (actionMap.activeActionMap)
        {
            case ActionMaps.Player:
            case ActionMaps.Vehicle:
                PlayerFollowMove();
                break;

            case ActionMaps.ControlUnits:
                FreeMovement();
                break;

            case ActionMaps.MapUI:
                CenterOnPlayer();
                break;
        }
    }

    private void PlayerFollowMove()
    {
        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);
        transform.position = playerTransform.Position;
        transform.rotation = playerTransform.Rotation;
    }

    private void FreeMovement()
    {
        if (!_entityManager.IsComponentEnabled<MovePlayerInput>(_playerEntity)) return;

        MovePlayerInput moveInput = _entityManager.GetComponentData<MovePlayerInput>(_playerEntity);
        Vector3 move = new Vector3(moveInput.moveInput.x, 0f, moveInput.moveInput.y);
        transform.position += move * 10f * Time.deltaTime;
    }

    private void CenterOnPlayer()
    {
        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);
        transform.position = playerTransform.Position;
    }
}
