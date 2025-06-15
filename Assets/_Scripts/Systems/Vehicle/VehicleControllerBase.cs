using UnityEngine;
using System;

// Todo: Remove reverse direction, calculate move direction for facings(possible make it a utility), Add horse controlls, create on off

[RequireComponent(typeof(Rigidbody))]
public class VehicleControllerBase : MonoBehaviour, IFixedUpdateObserver
{
    [Header("Dependencies")]
    [SerializeField] protected IInputProvider input;
    [SerializeField] protected Rigidbody rb;
    [SerializeField] protected RiveAnimator animator;
    //[SerializeField] private MonoBehaviour HorseFacingComponent;
    [SerializeField] private MonoBehaviour DriverFacingComponent;
    //private IFacingController facingController;


    [Header("Vehicle Profile")]
    [SerializeField] private VehicleProfiles vehicleProfiles;


    [Header("Starting State")]
    [SerializeField] private bool active = true;

    // Internal
    private float forwardInput = 0f;
    private float steerInput = 0f;
    private float currentForwardSpeed = 0f;
    private float currentTurnSpeed = 0f;


    [Header("Wheels")]
    [SerializeField]
    private WheelData[] wheels;   // THIS is what you’ll see in the Inspector

    /// <summary>
    /// Read-only access to wheel data for visuals.
    /// </summary>
    public WheelData[] Wheels => wheels;

    [Serializable]
    public struct WheelData
    {
        public Transform mesh;        // visible
        public bool isLeftSide;  // visible
        public float radius;      // visible

        [HideInInspector]
        public float targetRPM;       // hidden
    }


    void OnEnable()
    {
        FixedUpdateManager.RegisterObserver(this);

        if (DriverFacingComponent is IFacingController controller)
        {
            DriverFacingComponent = controller;
        }
        else
        {
            Debug.LogWarning($"{name}: Driver Facing component doesn't implement IFacingController.");
        }
    }

    void OnDisable() => FixedUpdateManager.UnregisterObserver(this);


    public void ObservedFixedUpdate()
    {
        if (active)
        {
            UpdateInputs();
            HandleMovement();
            ComputeWheelSpinTargets();

            // Needed to make sure billboard aline correctly
            UpdateHorsePosition(); // Horse
            UpdateDriverPosition(); // Driver

            // Changes which way they are facing animation wise
            HandleFacing(); // Horse
            HandleFacing(); // Driver
        }

        UpdateHorseAnimation();
    }

    // Private Methods
    private void UpdateInputs()
    {
        forwardInput = input.MoveInput.y;
        steerInput = input.MoveInput.x;
    }

    // Updates the movement of the vehicle
    protected virtual void HandleMovement()
    {
        float targetForward = CalculateTargetForwardSpeed();
        float targetTurn    = CalculateTargetTurnSpeed();

        UpdateForwardSpeed(targetForward);
        UpdateTurnSpeed(targetTurn);

        ApplyTranslation();
        ApplyRotation();
    }

    private void ComputeWheelSpinTargets()
    {
        bool usingForward = Mathf.Abs(currentForwardSpeed) > 0.01f;
        bool usingSteer = !usingForward && Mathf.Abs(currentTurnSpeed) > 0.01f;

        for (int i = 0; i < wheels.Length; i++)
        {
            float rpm = 0f;
            var w = wheels[i];

            if (usingForward)
            {
                // use currentForwardSpeed (m/s) → RPM
                rpm = currentForwardSpeed / (2f * Mathf.PI * w.radius) * 60f;
            }
            else if (usingSteer)
            {
                float spinSign = w.isLeftSide ? +1f : -1f;
                // use currentTurnSpeed (deg/s) scaled to an RPM-like value
                rpm = spinSign * currentTurnSpeed;
            }

            wheels[i].targetRPM = rpm;
        }
    }


    // 2d Visual Updates
    // Moves the horse plane depending on the direction
    protected virtual void UpdateHorsePosition()
    {
        Debug.Log("Updated Driver Location");
    }

    // Moves the Driver plane depending on the direction
    protected virtual void UpdateDriverPosition()
    {
        Debug.Log("Updated Driver Location");
    }

    // Changes the Horse/driver plane Facing depending on the direction
    protected virtual void HandleFacing()
    {
        if (!isMoving || DriverFacingComponent == null)
            return;

        DriverFacingComponent.UpdateFacing(moveDirection);
    }

    protected virtual void UpdateHorseAnimation()
    {
        Debug.Log("Updated Horse");
    }


    // Public enable/disable
    public virtual void ActivateVehicle() => active = true;
    public virtual void DeactivateVehicle() => active = false;

    // Helper Methods
    private float CalculateTargetForwardSpeed()
    {
        return forwardInput * vehicleProfiles.moveSpeed;
    }
    private float CalculateTargetTurnSpeed()
    {
        float turnFactor = Mathf.Approximately(forwardInput, 0f)
            ? vehicleProfiles.idleTurnSpeedFactor
            : 1f;
        return steerInput * vehicleProfiles.turnSpeed * turnFactor;
    }

    private void UpdateForwardSpeed(float targetSpeed)
    {
        float accel = (Mathf.Abs(targetSpeed) > Mathf.Abs(currentForwardSpeed))
            ? vehicleProfiles.forwardAcceleration : vehicleProfiles.forwardDeceleration;
        currentForwardSpeed = Mathf.MoveTowards(
            currentForwardSpeed, targetSpeed, accel * Time.fixedDeltaTime
        );
    }

    private void UpdateTurnSpeed(float targetSpeed)
    {
        float accel = (Mathf.Abs(targetSpeed) > Mathf.Abs(currentTurnSpeed))
            ? vehicleProfiles.turnAcceleration : vehicleProfiles.turnDeceleration;
        currentTurnSpeed = Mathf.MoveTowards(
            currentTurnSpeed, targetSpeed, accel * Time.fixedDeltaTime
        );
    }

    private void ApplyTranslation()
    {
        Vector3 delta = transform.forward * currentForwardSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + delta);
    }

    private void ApplyRotation()
    {
        float yDeg = currentTurnSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0, yDeg, 0));
    }
}