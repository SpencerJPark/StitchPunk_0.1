// Copyright (c) 2026 Stitch Punk. All rights reserved.

using DotsAnimationToolkit;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkitShaderDemo
{
    /// <summary>
    /// Turns a plain <see cref="Transform"/> with the toolkit's own billboard maths, so the CPU
    /// facing convention can be judged on screen without a rig, a bake or an ECS world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Verification scaffolding, not shipped behaviour.</strong> Nothing in the game or the
    /// package uses this. It exists to answer one question that no automated test can:
    /// amendment A44 corrected the billboard facing sign so that a node's local +Z points
    /// <em>away</em> from the viewer, on the grounds that Unity's <c>PrimitiveType.Quad</c> carries
    /// its visible normal on −Z. Every test in the suite can prove the three code paths agree with
    /// each other; none can prove they agree with a mesh, because the package cannot see one.
    /// </para>
    /// <para>
    /// <strong>It calls <see cref="BillboardMath.TryResolve"/> directly</strong> — the same function
    /// <c>BillboardResolveSystem</c> calls, with the same settings struct. That is the whole point:
    /// a probe that reimplemented the rotation would answer a question about itself.
    /// </para>
    /// <para>
    /// <c>LateUpdate</c>, so the orbiting camera has already moved this frame. Billboarding against
    /// last frame's camera is a lag nobody would attribute to the right cause.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("DOTS Animation Toolkit/Billboard Sign Probe")]
    public sealed class BillboardSignProbe : MonoBehaviour
    {
        [Tooltip("Which billboard rule to apply. Full and ScreenAligned are the interesting ones.")]
        public BillboardMode mode = BillboardMode.Full;

        [Tooltip("Camera to face. Leave empty to use Camera.main.")]
        public Camera targetCamera;

        [Tooltip("Axis for AxisConstrained mode; ignored by every other mode.")]
        public Vector3 constraintAxis = Vector3.up;

        [Tooltip("Rotation off the resolved facing, about the billboard frame's own up axis.")]
        public float angleOffsetDegrees;

        [Tooltip("0 leaves the authored rotation alone, 1 is fully billboarded.")]
        [Range(0f, 1f)] public float blendWeight = 1f;

        /// <summary>
        /// The orientation this transform had before billboarding, captured once.
        /// </summary>
        /// <remarks>
        /// Billboarding replaces a node's rotation rather than accumulating onto it, so the rest
        /// orientation has to be remembered — reading the live rotation each frame would feed the
        /// previous frame's result back in as this frame's rest pose and the blend would creep.
        /// </remarks>
        private quaternion restRotation = quaternion.identity;
        private bool hasRestRotation;

        private void OnEnable()
        {
            restRotation = transform.rotation;
            hasRestRotation = true;

#if UNITY_EDITOR
            // Same reason ToolkitOrbitCamera does this: outside play mode Unity ticks an
            // [ExecuteAlways] component only when something dirties the scene, and "face the camera"
            // never does. Driving off the editor loop is what makes the probe follow the orbiting
            // camera instead of holding whatever rotation it had when the scene opened.
            //
            // Both components share that loop and the subscription order decides which runs first,
            // so the probe may face where the camera was one tick ago. At orbit speeds that is far
            // below perception, and the alternative — an explicit ordering contract between two
            // pieces of scratch scaffolding — would cost more than the frame it saves.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.update += EditorTick;
            }
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
        }

#if UNITY_EDITOR
        private void EditorTick()
        {
            if (this == null)
            {
                UnityEditor.EditorApplication.update -= EditorTick;
                return;
            }
            if (!Application.isPlaying)
            {
                Billboard();
            }
        }
#endif

        private void LateUpdate()
        {
            Billboard();
        }

        private void Billboard()
        {
            Camera camera = targetCamera != null ? targetCamera : Camera.main;
            if (camera == null)
            {
                return;
            }
            if (!hasRestRotation)
            {
                restRotation = transform.rotation;
                hasRestRotation = true;
            }

            BillboardSettings settings = new BillboardSettings
            {
                mode = mode,
                constraintAxis = constraintAxis,
                frozenYaw = 0f,
                angleOffsetRadians = math.radians(angleOffsetDegrees),
                blendWeight = blendWeight,
                enabled = true,
                snapSteps = 0,
                snapPhaseRadians = 0f,
                clampHalfArcRadians = -1f
            };

            Transform cameraTransform = camera.transform;
            quaternion resolvedRotation;
            if (BillboardMath.TryResolve(
                    settings,
                    transform.position,
                    cameraTransform.position,
                    cameraTransform.forward,
                    restRotation,
                    out resolvedRotation))
            {
                transform.rotation = resolvedRotation;
            }
        }
    }
}
