// Copyright (c) 2026 Stitch Punk. All rights reserved.

using StitchPunk.AnimationToolkit;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkitShaderDemo
{
    /// <summary>
    /// Feeds the rendering camera to the toolkit — the host-side half of the camera contract, and
    /// the reference implementation for anyone integrating the package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The package deliberately never reads a <c>Camera</c>.</strong> It cannot know which
    /// of a host's cameras is the one that matters, and a package that grabbed
    /// <c>Camera.main</c> would be wrong in every project with a minimap, a portal, or a render
    /// texture. So the host writes the camera in, and this component is the smallest correct way to
    /// do it.
    /// </para>
    /// <para>
    /// It writes <strong>two</strong> things, and both are needed for different reasons:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <c>AnimationToolkitCameraData</c> — the ECS singleton. <c>AnimLodDistanceSystem</c> measures
    /// distance from <c>position</c>, and since amendment A44 <c>BillboardResolveSystem</c> reads
    /// both fields: <c>forward</c> for screen-aligned roots and <c>position</c> for spherical ones.
    /// <strong>Without this component nothing billboards at all</strong> — the package never reads a
    /// <c>Camera</c>, because it cannot know which of a host's cameras matters.
    /// </description></item>
    /// <item><description>
    /// <c>_ToolkitCameraForward</c> — the shader global that screen-aligned billboarding reads
    /// (amendment A39). It has to be a global rather than a per-instance property because it is the
    /// same for every quad, and per-instance data that never varies is pure batching cost (§6.6).
    /// </description></item>
    /// </list>
    /// <para>
    /// <strong>Written in <c>LateUpdate</c>, deliberately.</strong> A camera controller that moves in
    /// <c>Update</c> would otherwise be sampled one frame stale, and a one-frame-late billboard on a
    /// moving camera reads as a shimmer rather than as a lag.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("DOTS Animation Toolkit/Toolkit Camera Binder")]
    public sealed class ToolkitCameraBinder : MonoBehaviour
    {
        private static readonly int CameraForwardPropertyId = Shader.PropertyToID("_ToolkitCameraForward");

        [Tooltip("The camera the toolkit should treat as the viewer. Defaults to this GameObject's camera.")]
        public Camera sourceCamera;

        private void LateUpdate()
        {
            Camera camera = sourceCamera != null ? sourceCamera : GetComponent<Camera>();
            if (camera == null)
            {
                return;
            }

            Transform cameraTransform = camera.transform;
            float3 cameraPosition = cameraTransform.position;
            float3 cameraForward = cameraTransform.forward;

            // The shader global. Set even in edit mode so scene-view previews of billboarded
            // material are not silently spherical while the game view is screen-aligned.
            Shader.SetGlobalVector(
                CameraForwardPropertyId,
                new Vector4(cameraForward.x, cameraForward.y, cameraForward.z, 0f));

            WriteCameraSingleton(cameraPosition, cameraForward);
        }

        /// <summary>
        /// Creates or updates the <see cref="AnimationToolkitCameraData"/> singleton.
        /// </summary>
        /// <remarks>
        /// Tolerates every stage of world setup rather than assuming one: no world yet (domain
        /// reload, a scene opened without Entities), a world with no singleton (first frame), and the
        /// steady state. A binder that threw during any of those would be a component nobody could
        /// leave in a scene.
        /// </remarks>
        private static void WriteCameraSingleton(float3 cameraPosition, float3 cameraForward)
        {
            World defaultWorld = World.DefaultGameObjectInjectionWorld;
            if (defaultWorld == null || !defaultWorld.IsCreated)
            {
                return;
            }

            EntityManager entityManager = defaultWorld.EntityManager;
            EntityQuery cameraQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<AnimationToolkitCameraData>());

            AnimationToolkitCameraData cameraData = new AnimationToolkitCameraData
            {
                position = cameraPosition,
                forward = cameraForward
            };

            if (cameraQuery.CalculateEntityCount() > 0)
            {
                cameraQuery.SetSingleton(cameraData);
                return;
            }
            entityManager.CreateSingleton(cameraData, "AnimationToolkitCamera");
        }
    }

    /// <summary>
    /// Orbits a camera around a point, so billboard modes can be judged under motion.
    /// </summary>
    /// <remarks>
    /// Verification scaffolding, not shipped behaviour. It exists because §11.4 asks for billboard
    /// modes to be human-verified <em>under camera orbit</em>, and the Stitch Punk camera tilts and
    /// rotates but never orbits — so the game itself cannot produce that evidence. Judging a
    /// billboard from a still frame is close to impossible: every mode looks correct head-on, and
    /// they differ only in how they behave as the viewer moves.
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("DOTS Animation Toolkit/Toolkit Orbit Camera")]
    public sealed class ToolkitOrbitCamera : MonoBehaviour
    {
        [Tooltip("Point to orbit around.")]
        public Vector3 target = Vector3.zero;

        [Tooltip("Distance from the target.")]
        public float radius = 6f;

        [Tooltip("Height above the target.")]
        public float height = 1.5f;

        [Tooltip("Degrees per second. Negative reverses.")]
        public float degreesPerSecond = 30f;

        [Tooltip("Orbit in edit mode as well as play mode.")]
        public bool orbitInEditMode;

        private float currentAngleDegrees;

        private void Update()
        {
            if (!Application.isPlaying && !orbitInEditMode)
            {
                return;
            }

            currentAngleDegrees += degreesPerSecond * Time.deltaTime;
            float angleRadians = currentAngleDegrees * Mathf.Deg2Rad;

            Vector3 orbitPosition = target + new Vector3(
                Mathf.Sin(angleRadians) * radius,
                height,
                Mathf.Cos(angleRadians) * radius);

            transform.position = orbitPosition;
            transform.LookAt(target, Vector3.up);
        }
    }
}
