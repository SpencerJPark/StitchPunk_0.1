using UnityEngine;

[ExecuteAlways]
public class SkewController : MonoBehaviour, ILateUpdateObserver
{
    [Header("References")]
    public Camera targetCamera;

    [Header("Skew Settings")]
    [Tooltip("Max camera X tilt (deg) that maps to full skew.")]
    [Range(1, 90)] public float maxXTilt = 35f;
    [Tooltip("Min delta (deg) before we recalc/SetGlobal.")]
    public float angleThreshold = 0.5f;

    // State
    float lastCamX;
    float lastCamY;

    void OnEnable()
    {
        LateUpdateManager.RegisterObserver(this);

        if (targetCamera == null) targetCamera = Camera.main;
        CacheAnglesAndPush();
    }

    void OnDisable()
    {
        LateUpdateManager.UnregisterObserver(this);
    }

    public void ObservedLateUpdate()
    {
        
        if (targetCamera == null) return;

        var e = targetCamera.transform.eulerAngles;
        float camX = e.x;
        float camY = e.y;

        // compute smallest delta (handles wrap at 360)
        float dx = Mathf.DeltaAngle(lastCamX, camX);
        float dy = Mathf.DeltaAngle(lastCamY, camY);

        if (Mathf.Abs(dx) > angleThreshold || Mathf.Abs(dy) > angleThreshold)
        {
            CacheAnglesAndPush();
        }
    }

    void CacheAnglesAndPush()
    {
        var e = targetCamera.transform.eulerAngles;
        lastCamX = e.x;
        lastCamY = e.y;

        // 1) Normalize X tilt: 0°→0, maxXTilt→1
        float normX = Mathf.Clamp01(e.x / maxXTilt);

        // 2) Compute blend weights from Y: 
        //    at Y=0° → (_SkewX=0, _SkewZ=1)
        //    at Y=90°→ (_SkewX=1, _SkewZ=0)
        float rad = e.y * Mathf.Deg2Rad;
        float skewX = normX * Mathf.Sin(rad);
        float skewZ = normX * Mathf.Cos(rad);

        // 3) Push to all materials that use our graph:
        Shader.SetGlobalFloat("_SkewX", skewX);
        Shader.SetGlobalFloat("_SkewZ", skewZ);
    }
}
