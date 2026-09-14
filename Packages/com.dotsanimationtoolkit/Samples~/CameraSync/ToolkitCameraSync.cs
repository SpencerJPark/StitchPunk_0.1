// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace DotsAnimationToolkit.Samples
{
    /// <summary>
    /// Writes the <c>AnimationToolkitCameraData</c> singleton every LateUpdate from one camera's transform,
    /// which billboarded rigs and distance LOD both wait for. Add it to any scene object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolkitCameraSync : MonoBehaviour
    {
        [Tooltip("The camera billboards face and distance LOD measures from. Leave empty to follow " +
            "Camera.main, the camera tagged MainCamera at the moment.")]
        public Camera cameraOverride;

        private World cameraDataWorld;
        private Entity cameraDataEntity = Entity.Null;

        private void LateUpdate()
        {
            Camera sourceCamera = cameraOverride != null ? cameraOverride : Camera.main;
            if (sourceCamera == null)
            {
                return;
            }

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            EntityManager entityManager = world.EntityManager;

            // A host can dispose and recreate the default world, and an Entity kept from the old world
            // can name an unrelated live entity in the new one, so the world itself is compared too.
            if (world != cameraDataWorld
                || !entityManager.Exists(cameraDataEntity)
                || !entityManager.HasComponent<AnimationToolkitCameraData>(cameraDataEntity))
            {
                cameraDataEntity = FindOrCreateCameraDataEntity(entityManager);
                cameraDataWorld = world;
            }

            // Forward comes from the transform, not a render matrix, so it is the same in every render
            // pass, including the shadow pass, where the view matrix belongs to the light.
            Transform cameraTransform = sourceCamera.transform;
            entityManager.SetComponentData(cameraDataEntity, new AnimationToolkitCameraData
            {
                position = cameraTransform.position,
                forward = cameraTransform.forward
            });
        }

        private static Entity FindOrCreateCameraDataEntity(EntityManager entityManager)
        {
            EntityQuery cameraDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<AnimationToolkitCameraData>());
            if (!cameraDataQuery.IsEmptyIgnoreFilter)
            {
                using (NativeArray<Entity> existingEntities = cameraDataQuery.ToEntityArray(Allocator.Temp))
                {
                    return existingEntities[0];
                }
            }

            return entityManager.CreateSingleton(new AnimationToolkitCameraData(), "AnimationToolkitCameraData");
        }
    }
}
