using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class PlayerEcsTracker : MonoBehaviour
{
    private EntityManager _entityManager;
    private EntityQuery _playerQuery;
    private Entity _playerEntity;
    private bool _hasPlayer;

    private void Awake()
    {
        // Get the default ECS world’s EntityManager
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // Query for the player entity (assuming you only ever have one)
        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerCharacter>(),
            ComponentType.ReadOnly<LocalTransform>());
    }

    private void Update()
    {
        // Lazy-bind the player entity in case it spawns later
        if (!_hasPlayer)
        {
            if (_playerQuery.IsEmpty)
                return;

            _playerEntity = _playerQuery.GetSingletonEntity();
            _hasPlayer = true;
        }

        if (!_entityManager.Exists(_playerEntity))
        {
            _hasPlayer = false;
            return;
        }

        // Read LocalTransform from ECS
        LocalTransform playerTransform = _entityManager.GetComponentData<LocalTransform>(_playerEntity);

        // Apply to this GameObject
        transform.position = playerTransform.Position;
        transform.rotation = playerTransform.Rotation;
        // if you need scale, you can also apply playerTransform.Scale
    }
}