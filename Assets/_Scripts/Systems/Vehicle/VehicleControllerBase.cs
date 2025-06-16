using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody))]
public class VehicleControllerBase : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Dependencies")]
    [SerializeField] protected IInputProvider input;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected RiveAnimator animator;
    [SerializeField] private MonoBehaviour driverFacingComponet;
    [SerializeField] private IFacingController driverFacingController;

    [Header("Vehicle Profile")]
    [SerializeField] private VehicleProfiles vehicleProfiles;

    [Header("Starting State")]
    [SerializeField] private bool active = true;

    // Movement state
    private Vector2 moveInput;
    private float   currentSpeed;
    private Vector3 currentDirection = Vector3.forward;
    private float   turnVelocity;

    [Header("Wheels")]
    [SerializeField] private WheelData[] wheels;
    public WheelData[] Wheels => wheels;

    [Serializable]
    public struct WheelData
    {
        public Transform mesh;
        public bool      isLeftSide;
        public float     radius;
        [HideInInspector]
        public float     targetRPM;
    }

    void OnEnable()
    {
        FixedUpdateManager.RegisterObserver(this);

        if (driverFacingComponet is IFacingController fc)
        driverFacingController = fc;
        else
        Debug.LogWarning($"[{name}] driverFacingBehaviour doesn't implement IFacingController");
    }
    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);

    public void ObservedFixedUpdate()
    {
        if (!active) return;

        ReadInput();
        HandleMovement();
        ComputeWheelSpinTargets();

        UpdateHorsePosition();
        UpdateDriverPosition();

        if (driverFacingController != null && rb.linearVelocity.sqrMagnitude > 0.01f)
        driverFacingController.UpdateFacing(currentDirection);

        UpdateHorseAnimation();
    }

    private void ReadInput()
    {
        moveInput = input.MoveInput;
    }

    protected virtual void HandleMovement()
    {
        Vector3 desiredVelocity = ComputeDesiredVelocity();
        float   targetSpeed     = desiredVelocity.magnitude;
        Vector3 targetDirection = GetTargetDirection(desiredVelocity);

        UpdateSpeed(targetSpeed);
        UpdateDirection(targetDirection);
        ApplyMovement();
    }

    private Vector3 ComputeDesiredVelocity()
    {
        return new Vector3(moveInput.x, 0f, moveInput.y) * vehicleProfiles.moveSpeed;
    }

    private Vector3 GetTargetDirection(Vector3 desiredVelocity)
    {
        return desiredVelocity.sqrMagnitude > 0.0001f
             ? desiredVelocity.normalized
             : currentDirection;
    }

    private void UpdateSpeed(float targetSpeed)
    {
        float accel = (targetSpeed > currentSpeed)
            ? vehicleProfiles.forwardAcceleration
            : vehicleProfiles.forwardDeceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            accel * Time.fixedDeltaTime
        );
    }

    private void UpdateDirection(Vector3 targetDirection)
    {
        float currentAngle = Mathf.Atan2(currentDirection.x, currentDirection.z) * Mathf.Rad2Deg;
        float targetAngle  = Mathf.Atan2(targetDirection.x,  targetDirection.z)  * Mathf.Rad2Deg;

        float smoothAngle = Mathf.SmoothDampAngle(
            currentAngle,
            targetAngle,
            ref turnVelocity,
            vehicleProfiles.turnSmoothTime,
            vehicleProfiles.turnSpeed,
            Time.fixedDeltaTime
        );

        currentDirection = (Quaternion.Euler(0f, smoothAngle, 0f) * Vector3.forward).normalized;
    }

    private void ApplyMovement()
    {
        Vector3 velocity = currentDirection * currentSpeed;
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);

        if (velocity.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(currentDirection, Vector3.up);
            rb.MoveRotation(
                Quaternion.RotateTowards(
                    rb.rotation,
                    look,
                    vehicleProfiles.turnSpeed * Time.fixedDeltaTime
                )
            );
        }
    }

    private void ComputeWheelSpinTargets()
    {
        float speed = currentSpeed;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            float rpm = speed > 0.01f
                ? (speed / (2f * Mathf.PI * w.radius)) * 60f
                : 0f;
            wheels[i].targetRPM = rpm;
        }
    }

    // Visual & facing stubs
    protected virtual void UpdateHorsePosition()   { Debug.Log("Updated Horse Position"); }
    protected virtual void UpdateDriverPosition()  { Debug.Log("Updated Driver Position"); }
    protected virtual void HandleFacing(IFacingController controller)
    {
        if (currentSpeed == 0.0f || controller == null)
            return;

        controller.UpdateFacing(currentDirection);
    }
    protected virtual void UpdateHorseAnimation()  { Debug.Log("Updated Horse Animation"); }

    // Public controls
    public virtual void ActivateVehicle()   => active = true;
    public virtual void DeactivateVehicle() => active = false;
}
