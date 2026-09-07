using DotsAnimationToolkit;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Writes <see cref="AnimationToolkitCameraData"/> every LateUpdate from Camera.main's transform —
/// the package never reads a Camera itself (verify-billboarding.md), and without a writer
/// BillboardResolveSystem never runs at all. Camera.main already reflects whichever Cinemachine
/// vcam is currently blended in (player, cutscene, map, ...), so one writer covers every camera
/// the game has without per-vcam-type logic.
/// </summary>
public class AnimationToolkitCameraBridge : MonoBehaviour
{
    private EntityManager entityManager;
    private Entity cameraDataEntity;
    private bool worldReady;

    private void LateUpdate()
    {
        if (!EnsureWorldReady())
            return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        Transform cameraTransform = mainCamera.transform;
        entityManager.SetComponentData(cameraDataEntity, new AnimationToolkitCameraData
        {
            position = cameraTransform.position,
            forward = cameraTransform.forward
        });
    }

    private bool EnsureWorldReady()
    {
        if (worldReady) return true;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        entityManager = world.EntityManager;
        EntityQuery cameraDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AnimationToolkitCameraData>());
        cameraDataEntity = cameraDataQuery.CalculateEntityCount() == 1
            ? cameraDataQuery.GetSingletonEntity()
            : entityManager.CreateEntity(typeof(AnimationToolkitCameraData));
        worldReady = true;
        return true;
    }
}
