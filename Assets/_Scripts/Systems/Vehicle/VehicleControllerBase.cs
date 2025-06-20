using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody))]
public class VehicleControllerBase : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Dependencies")]
    [SerializeField] protected IInputProvider input;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected RiveAnimator animator;
    [SerializeField] private FacingDirectionBase driverFacingController;
    [SerializeField] private FacingDirectionBase horseFacingController;


    [Header("Vehicle Profile")]
    [SerializeField] private VehicleProfiles vehicleProfiles;


    [Header("2D Visuals")]
    [SerializeField] private Transform            horseQuad;
    [SerializeField] private FacingOffsetProfile  horseOffsetProfile;
    [SerializeField] private FacingDirectionBase  horseFacing;
    [SerializeField] private Transform            driverQuad;
    [SerializeField] private FacingOffsetProfile  driverOffsetProfile;
    [SerializeField] private FacingDirectionBase  driverFacing;
    


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
    }

    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);

    public void ObservedFixedUpdate()
    {
        if (!active) return;

        ReadInput();
        HandleMovement();
        ComputeWheelSpinTargets();

        Handle2D();
    }

    void Start()
{
    // 1) Lower the COM so the pivot is closer to the ground:
    rb.centerOfMass = new Vector3(0, -1.0f, 0);  // tweak Y until it feels stable
    
    // 2) Increase angular drag so flips damp out instantly:
    rb.angularDamping = 10f;                        // higher = more resistance to spinning
    
    // (Optional) Manually boost inertia on X/Z so it “weighs” more against tipping:
    rb.inertiaTensor = new Vector3(1000f, 2f, 1000f);
    rb.inertiaTensorRotation = Quaternion.identity;
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


    // 2D Visuals
   protected virtual void Handle2D()
    {
        UpdateHorsePosition();
        UpdateDriverPosition();

        driverFacingController.UpdateFacing(currentDirection);
        horseFacingController.UpdateFacing(currentDirection);
        
        UpdateHorseAnimation();
    }

    protected virtual void UpdateHorsePosition()
    {
        var dir = horseFacing.CurrentDirection;
        horseQuad.localPosition = horseOffsetProfile.GetOffset(dir);
    }

    protected virtual void UpdateDriverPosition()
    {
        var dir = driverFacing.CurrentDirection;
        driverQuad.localPosition = driverOffsetProfile.GetOffset(dir);
    }

    protected virtual void UpdateHorseAnimation()
    {
        if (animator == null || vehicleProfiles == null)
            return;

        // normalize your speed into [0,1]
        float t = currentSpeed / vehicleProfiles.moveSpeed;
        string action;

        if (t <= vehicleProfiles.walkThreshold)
            action = Actions.Idle.ToString();
        else if (t <= vehicleProfiles.runThreshold)
            action = Actions.Walk.ToString();
        else
            action = Actions.Run.ToString();

        animator.SetEnum("Actions", action);
    }
 

    // Public controls
    public virtual void ActivateVehicle() => active = true;
    public virtual void DeactivateVehicle() => active = false;
}
