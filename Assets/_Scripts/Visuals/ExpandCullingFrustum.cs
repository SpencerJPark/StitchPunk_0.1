using UnityEngine;

[DefaultExecutionOrder(100)]  // keep this so it’s always after CinemachineBrain
[RequireComponent(typeof(Camera))]
public class ExpandCullingFrustum : MonoBehaviour
{
    [Tooltip("The FOV to use *just* for culling — your real FOV stays under Cinemachine’s control.")]
    public float cullFieldOfView = 90f;

    Camera _cam;

    void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    void OnPreCull()
    {
        // Compute a “fatter” projection matrix for culling only
        float aspect = _cam.aspect;
        float near = _cam.nearClipPlane;
        float far  = _cam.farClipPlane;

        // Build a perspective matrix with your expanded FOV
        Matrix4x4 cullProj = Matrix4x4.Perspective(
            cullFieldOfView,
            aspect,
            near,
            far
        );

        // The camera internally culls by cullingMatrix * worldToCameraMatrix.
        _cam.cullingMatrix = cullProj * _cam.worldToCameraMatrix;
    }

    void OnPreRender()
    {
        // Restore Unity’s default (projectionMatrix × worldToCameraMatrix)
        _cam.ResetCullingMatrix();
    }
}
