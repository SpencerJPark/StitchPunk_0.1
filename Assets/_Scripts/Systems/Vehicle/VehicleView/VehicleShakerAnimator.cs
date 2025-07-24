using UnityEngine;

[RequireComponent(typeof(Transform))]
public class VehicleShakerAnimator : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Source (auto‑found on parent if blank)")]
    [SerializeField] private VehicleControllerBase controller;
    [SerializeField] private Rigidbody              vehicleRb;

    [Header("Speed‑based bounce")]
    [SerializeField] private float maxSpeed       = 10f;
    [SerializeField] private float speedTiltAngle = 5f;
    [SerializeField] private float noiseFrequency = 1f;

    [Header("Turn‑based lean")]
    [SerializeField] private float turnTiltAngle = 10f;
    [SerializeField] private float maxYawRate    = 2f;

    [Header("Tilt Ranges")]
    [Tooltip("Clamp how far (in degrees) you pitch/bounce (X‑axis)")]
    [SerializeField] private float rangeX = 8f;
    [Tooltip("Clamp how far (in degrees) you roll/lean (Z‑axis)")]
    [SerializeField] private float rangeZ = 12f;

    private Quaternion _restRot;

    void Awake()
    {
        _restRot = transform.localRotation;
        if (controller == null)
            controller = GetComponentInParent<VehicleControllerBase>();
        if (vehicleRb == null && controller != null)
            vehicleRb = controller.GetComponent<Rigidbody>();
    }
    
    void OnEnable() => FixedUpdateManager.RegisterObserver(this);
    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);

    public void ObservedFixedUpdate()
    {
        if (controller == null || vehicleRb == null)
            return;

        // 1) Compute “raw” pitch & roll from speed + turn
        float speedNorm = Mathf.Clamp01(controller.currentSpeed / maxSpeed);
        float n1 = Mathf.PerlinNoise(Time.time * noiseFrequency, 0f) - 0.5f;
        float rawPitch = n1 * 2f * speedTiltAngle * speedNorm;

        float yawRate = vehicleRb.angularVelocity.y;
        float yawNorm = Mathf.Clamp(yawRate / maxYawRate, -1f, 1f);
        float n2 = Mathf.PerlinNoise(1f, Time.time * noiseFrequency) - 0.5f;
        float rawRoll = n2 * 2f * speedTiltAngle * speedNorm
                        - yawNorm * turnTiltAngle;

        // 2) Clamp into your explicit ranges
        float pitchAmt = Mathf.Clamp(rawPitch, -rangeX, rangeX);
        float rollAmt = Mathf.Clamp(rawRoll, -rangeZ, rangeZ);

        // 3) Reset and apply tilt around local X & Z
        transform.localRotation = _restRot;
        transform.Rotate(transform.right, pitchAmt, Space.World);
        transform.Rotate(transform.forward, rollAmt, Space.World);
    }
}
