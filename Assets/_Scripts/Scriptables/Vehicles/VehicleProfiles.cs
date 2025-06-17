using UnityEngine;

[CreateAssetMenu(fileName = "VehicleProfile", menuName = "Vehicle/Vehicle Profile", order = 1)]
public class VehicleProfiles : ScriptableObject
{
    [Header("Speed Settings")]
    public float moveSpeed = 7f;
    public float turnSpeed = 50f;

    [Header("Acceleration/Deceleration")]
    public float forwardAcceleration = 2;
    public float forwardDeceleration = 3;
    public float turnAcceleration = 200;
    public float turnDeceleration = 150;
    public float idleTurnSpeedFactor = 0.4f;

    [Tooltip("Time (in seconds) it takes to ease into new facing direction; lower = quicker snap")]
    public float turnSmoothTime = 0.1f;

    [Header("Animation Thresholds")]
    // As a fraction of max speed: below walkThreshold → Idle; between walk/run → Walk; above runThreshold → Run
    public float walkThreshold = 0.1f;
    public float runThreshold  = 0.7f;


}
