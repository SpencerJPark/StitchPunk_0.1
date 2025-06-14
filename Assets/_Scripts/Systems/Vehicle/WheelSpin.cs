using UnityEngine;

public class WheelSpin : MonoBehaviour, IUpdateObserver
{
    [SerializeField] private VehicleControllerBase controller;
    [SerializeField] private int wheelIndex;

    float currentAngle;

    void OnEnable()  => UpdateManager.RegisterObserver(this);
    void OnDisable() => UpdateManager.UnregisterObserver(this);

    public void ObservedUpdate()
    {
        // 1) Bounds-check your index
        var wheels = controller.Wheels;
        if (wheelIndex < 0 || wheelIndex >= wheels.Length)
            return;

        var w = wheels[wheelIndex];

        // 2) Protect against zero or negative radius
        if (w.radius <= 0f)
        {
            Debug.LogWarning($"Wheel {name} has invalid radius {w.radius}. Check your VehicleControllerBase settings.");
            return;
        }

        // 3) Compute delta, but guard against NaN/infinity
        float deltaAngle = w.targetRPM / 60f * 360f * Time.deltaTime;
        if (float.IsNaN(deltaAngle) || float.IsInfinity(deltaAngle))
            deltaAngle = 0f;

        // 4) Accumulate and wrap, then guard again
        currentAngle = (currentAngle + deltaAngle) % 360f;
        if (float.IsNaN(currentAngle) || float.IsInfinity(currentAngle))
            currentAngle = 0f;

        // 5) Finally apply a valid rotation
        transform.localRotation = Quaternion.Euler(currentAngle, 0f, 0f);
    }

}
