using UnityEngine;

[CreateAssetMenu(fileName = "VehicleData", menuName = "Vehicle/Vehicle Data", order = 1)]
public class VehicleData : ScriptableObject
{
    [field: SerializeField] public string VehicleName { get; private set; }
    [field: SerializeField] public int MaxVehicleHealth { get; private set; }
    [field: SerializeField] public bool ElectricYesNo { get; private set; }
    [field: SerializeField] public float MaxVoltage { get; private set; }


    public VehicleMovementData vehicleMovementData;

    [Header("Facing Profiles")]
    public FacingOffsetProfile driverOffsetProfile;
    public FacingOffsetProfile horseOffsetProfile;
}
