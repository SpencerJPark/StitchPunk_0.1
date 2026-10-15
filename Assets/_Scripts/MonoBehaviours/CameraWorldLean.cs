using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Leans the whole rendered world away from the camera so 3D meshes read on the same plane as the
/// upright sprite billboards. View-only: transforms, physics, lighting and shadow maps stay upright.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class CameraWorldLean : MonoBehaviour
{
    [Tooltip("Fraction of the camera's pitch the world leans by. 0 = no lean, 1 = vertical surfaces face the camera.")]
    [Range(0f, 1f)]
    [SerializeField] private float leanStrength = 0.5f;

    [Tooltip("World height that stays fixed. Everything above it leans, everything at it stays put.")]
    [SerializeField] private float groundHeight = 0f;

    [Tooltip("Extra screen fraction kept for culling, so tall objects just off the bottom edge do not pop in.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float cullingMargin = 0.15f;

    private Camera leanedCamera;

    private void OnEnable()
    {
        leanedCamera = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += ApplyLeanBeforeCameraRenders;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= ApplyLeanBeforeCameraRenders;
        if (leanedCamera != null)
        {
            leanedCamera.ResetWorldToCameraMatrix();
            leanedCamera.ResetCullingMatrix();
        }
    }

    // beginCameraRendering runs after Cinemachine's LateUpdate and before URP culls this camera. The custom
    // matrix then persists into next frame's Update, where it still matches the not-yet-moved transform,
    // so ScreenPointToRay picking stays consistent with what is on screen.
    private void ApplyLeanBeforeCameraRenders(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera != leanedCamera)
            return;

        Transform cameraTransform = leanedCamera.transform;
        Matrix4x4 uprightWorldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
            * Matrix4x4.TRS(cameraTransform.position, cameraTransform.rotation, Vector3.one).inverse;

        Vector3 cameraForward = cameraTransform.forward;
        Vector3 flatForward = new Vector3(cameraForward.x, 0f, cameraForward.z);
        if (leanStrength <= 0f || flatForward.sqrMagnitude < 1e-6f)
        {
            leanedCamera.ResetWorldToCameraMatrix();
            leanedCamera.ResetCullingMatrix();
            return;
        }
        flatForward.Normalize();

        float cameraPitchRadians = Mathf.Asin(Mathf.Clamp(-cameraForward.y, -1f, 1f));
        float leanRadians = cameraPitchRadians * leanStrength;
        Matrix4x4 worldLean = BuildWorldLeanMatrix(flatForward, leanRadians, groundHeight);

        leanedCamera.worldToCameraMatrix = uprightWorldToCamera * worldLean;

        // Cull with the upright view: culling with the leaned one drops shadow casters (seen in the 2026-10-15
        // lean study). Leaned tops render higher on screen than their upright position, hence the widened frustum.
        Matrix4x4 widenedProjection = leanedCamera.projectionMatrix;
        float frustumWidenScale = 1f / (1f + cullingMargin);
        widenedProjection.m00 *= frustumWidenScale;
        widenedProjection.m11 *= frustumWidenScale;
        leanedCamera.cullingMatrix = widenedProjection * uprightWorldToCamera;
    }

    // Rotates world up toward the camera's flattened forward by leanRadians, pivoting on the ground plane,
    // which is the same rigid tilt a screen-aligned billboard gets. X and Z axes are untouched.
    private static Matrix4x4 BuildWorldLeanMatrix(Vector3 flatForward, float leanRadians, float pivotHeight)
    {
        float leanSine = Mathf.Sin(leanRadians);
        float leanCosine = Mathf.Cos(leanRadians);

        Matrix4x4 worldLean = Matrix4x4.identity;
        worldLean.SetColumn(1, new Vector4(flatForward.x * leanSine, leanCosine, flatForward.z * leanSine, 0f));
        worldLean.SetColumn(3, new Vector4(
            -flatForward.x * leanSine * pivotHeight,
            pivotHeight * (1f - leanCosine),
            -flatForward.z * leanSine * pivotHeight,
            1f));
        return worldLean;
    }
}
