// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace DotsAnimationToolkit.Samples
{
    /// <summary>
    /// Minimal host for a staged <c>CutsceneAsset</c>: finds the stage, starts it, drives the
    /// camera and mark movement every frame, releases one named hold, and cleans up on
    /// completion. Every piece here is the smallest thing that demonstrates the contract — a real
    /// game replaces the lerp-to-mark stand-in with its own pathfinding and the camera copy with
    /// its own camera rig.
    /// </summary>
    public sealed class CutsceneSampleHost : MonoBehaviour
    {
        [Tooltip("CutsceneAsset.StableId of the staged cutscene to play — read it off the asset " +
            "in the inspector (the Cutscene Editor's cast panel shows it), not typed by hand.")]
        [SerializeField] private ulong cutsceneKey;

        [Tooltip("Press to find the stage and start playback.")]
        [SerializeField] private KeyCode playKey = KeyCode.P;

        [Tooltip("Press once the cutscene is paused on a hold whose id matches holdIdToRelease.")]
        [SerializeField] private KeyCode releaseHoldKey = KeyCode.R;

        [Tooltip("Must match the hold id authored on the cutscene's holding event exactly.")]
        [SerializeField] private string holdIdToRelease = "Dialogue";

        [Tooltip("World-space units per second the stand-in walk moves a slot toward its mark. " +
            "Replace with real pathfinding in a real game — the toolkit never moves the entity itself.")]
        [SerializeField] private float markWalkSpeed = 3f;

        private Entity requestEntity = Entity.Null;

        private void Update()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }
            EntityManager entityManager = world.EntityManager;

            if (Input.GetKeyDown(playKey) && requestEntity == Entity.Null)
            {
                StartCutscene(entityManager);
            }

            if (requestEntity == Entity.Null || !entityManager.Exists(requestEntity))
            {
                requestEntity = Entity.Null;
                return;
            }

            if (Input.GetKeyDown(releaseHoldKey))
            {
                ReleaseHold(entityManager);
            }

            ApplyCameraPose(entityManager);
            WalkBoundSlotsToTheirMarks(entityManager);
            DrainEvents(entityManager);

            CutscenePlaybackState playbackState = entityManager.GetComponentData<CutscenePlaybackState>(requestEntity);
            if (playbackState.isComplete)
            {
                entityManager.DestroyEntity(requestEntity);
                requestEntity = Entity.Null;
            }
        }

        private void StartCutscene(EntityManager entityManager)
        {
            Entity stageEntity;
            if (!CutsceneApi.TryFindStage(entityManager, cutsceneKey, out stageEntity))
            {
                Debug.LogWarning("[CutsceneSampleHost] No staged cutscene matches cutsceneKey " + cutsceneKey + ".");
                return;
            }
            requestEntity = CutsceneApi.CreatePlayRequestFromStage(entityManager, stageEntity);
        }

        private void ReleaseHold(EntityManager entityManager)
        {
            Unity.Collections.FixedString64Bytes wantedHoldId = holdIdToRelease;
            Unity.Collections.FixedString64Bytes currentHoldId;
            if (!CutsceneApi.TryGetCurrentHoldId(entityManager, requestEntity, out currentHoldId)
                || !currentHoldId.Equals(wantedHoldId))
            {
                return;
            }
            entityManager.SetComponentData(requestEntity, new CutsceneHoldRelease { holdId = wantedHoldId });
            entityManager.SetComponentEnabled<CutsceneHoldRelease>(requestEntity, true);
        }

        /// <summary>Applies the toolkit's camera singleton to Camera.main while it is driven — the toolkit never touches Camera.main itself.</summary>
        private static void ApplyCameraPose(EntityManager entityManager)
        {
            EntityQuery cameraPoseQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<CutsceneCameraPose>());
            if (cameraPoseQuery.IsEmptyIgnoreFilter)
            {
                cameraPoseQuery.Dispose();
                return;
            }
            CutsceneCameraPose cameraPose = cameraPoseQuery.GetSingleton<CutsceneCameraPose>();
            cameraPoseQuery.Dispose();

            if (!cameraPose.isDriven || Camera.main == null)
            {
                return;
            }
            Camera.main.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
            Camera.main.fieldOfView = cameraPose.fieldOfView;
        }

        /// <summary>
        /// Stand-in for pathfinding: every bound Actor/Prop slot with an outstanding
        /// <see cref="CutsceneMoveToMark"/> order steps straight toward it. A real game replaces
        /// this with its own movement system — the toolkit only ever judges arrival, never moves
        /// the entity.
        /// </summary>
        private void WalkBoundSlotsToTheirMarks(EntityManager entityManager)
        {
            DynamicBuffer<CutsceneActorBinding> bindings = entityManager.GetBuffer<CutsceneActorBinding>(requestEntity);
            float step = markWalkSpeed * Time.deltaTime;
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                Entity boundEntity = bindings[bindingIndex].actorEntity;
                if (boundEntity == Entity.Null
                    || !entityManager.HasComponent<CutsceneMoveToMark>(boundEntity)
                    || !entityManager.IsComponentEnabled<CutsceneMoveToMark>(boundEntity)
                    || !entityManager.HasComponent<LocalTransform>(boundEntity))
                {
                    continue;
                }

                CutsceneMoveToMark order = entityManager.GetComponentData<CutsceneMoveToMark>(boundEntity);
                LocalTransform localTransform = entityManager.GetComponentData<LocalTransform>(boundEntity);
                float3 toMark = order.position - localTransform.Position;
                float distance = math.length(toMark);
                localTransform.Position = distance <= step
                    ? order.position
                    : localTransform.Position + toMark / distance * step;
                entityManager.SetComponentData(boundEntity, localTransform);
            }
        }

        /// <summary>Drains the request's event buffer. A real game would map eventKey onto sound/dialogue here instead of discarding it.</summary>
        private void DrainEvents(EntityManager entityManager)
        {
            if (!entityManager.IsComponentEnabled<AnimEventsPending>(requestEntity))
            {
                return;
            }
            DynamicBuffer<AnimEventOutput> events = entityManager.GetBuffer<AnimEventOutput>(requestEntity);
            events.Clear();
            entityManager.SetComponentEnabled<AnimEventsPending>(requestEntity, false);
        }
    }
}
