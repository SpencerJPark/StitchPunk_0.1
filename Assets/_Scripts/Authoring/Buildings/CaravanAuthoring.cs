using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Goes beside WorkstationAuthoring on the caravan's simulation object (Caravan_System.md). The art lives
// in the scene on Caravan.prefab and follows this entity through CaravanPresenter.
[RequireComponent(typeof(WorkstationAuthoring))]
public class CaravanAuthoring : MonoBehaviour
{
    [Header("Points (empty = the default measured off Caravan.prefab)")]
    public Transform driverSeat;
    [Tooltip("Stand here and press E to drive. Parking puts the player back here.")]
    public Transform boardPoint;
    public Transform interiorStart;
    public Transform tailgateEdge;
    public Transform hopLanding;
    public Transform smokeBurstSpawn;
    public Transform chimneyTop;

    [Header("Driving")]
    public float maxForwardSpeed = 7f;
    public float maxReverseSpeed = 2.5f;
    public float acceleration = 3.5f;
    public float brakeDeceleration = 9f;
    public float coastDeceleration = 1.5f;
    public float turnDegreesPerSecondAtFullSpeed = 55f;
    [Tooltip("A cart can't spin on the spot: turning fades to nothing as the caravan stops.")]
    public float turnDegreesPerSecondAtLowSpeed = 25f;
    [Tooltip("E only parks below this speed.")]
    public float parkMaxSpeed = 0.5f;
    public float wheelRadius = 0.79f;
    [Tooltip("Local Z of the front of the horse and the back doors; the wall check and crash box run between them.")]
    public float frontBumperLocalZ = 7f;
    public float rearBumperLocalZ = -3f;
    public float halfWidth = 1.75f;
    public float bumperGroundClearance = 0.4f;

    [Header("Doors")]
    public float doorOpenSeconds = 0.6f;
    public float doorCloseSeconds = 0.5f;
    public float doorBurstSeconds = 0.35f;

    [Header("Crash")]
    [Tooltip("Below this speed units are only pushed aside.")]
    public float minimumImpactSpeed = 2f;
    public float damagePerMetrePerSecond = 6f;
    public float stunSecondsPerMetrePerSecond = 0.25f;
    [Tooltip("Far enough that a unit hit dead centre clears the caravan's half-width.")]
    public float shoveDistance = 2.5f;
    public float shoveSeconds = 0.25f;
    public int selfDamagePerHit = 1;
    [Range(0f, 1f)] public float speedLossPerHit = 0.15f;
    public float rehitCooldownSeconds = 1f;

    [Header("Summon exits")]
    public float walkOutSeconds = 1.2f;
    public float hopSeconds = 0.45f;
    public float hopArcHeight = 0.5f;
    public float shakeBeforeBurstSeconds = 1.2f;
    public float shakeBeforeBurstAmplitude = 0.05f;
    public float burstShakeSeconds = 0.3f;
    public float burstShakeAmplitude = 0.14f;
    public float smokeCoverDelaySeconds = 0.35f;
    public float smokeLingerSeconds = 1.5f;

    public class Baker : Baker<CaravanAuthoring>
    {
        public override void Bake(CaravanAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Caravan
            {
                driverSeatLocalOffset = LocalOffsetOf(authoring, authoring.driverSeat, new float3(0f, 2.42f, 2.72f)),
                boardPointLocalOffset = LocalOffsetOf(authoring, authoring.boardPoint, new float3(-2.6f, 0f, 3f)),
                interiorStartLocalOffset = LocalOffsetOf(authoring, authoring.interiorStart, new float3(0f, 1.93f, -0.6f)),
                tailgateEdgeLocalOffset = LocalOffsetOf(authoring, authoring.tailgateEdge, new float3(0f, 1.93f, -2.35f)),
                hopLandingLocalOffset = LocalOffsetOf(authoring, authoring.hopLanding, new float3(0f, 0f, -3.6f)),
                smokeBurstSpawnLocalOffset = LocalOffsetOf(authoring, authoring.smokeBurstSpawn, new float3(0f, 0f, -4f)),
                chimneyTopLocalOffset = LocalOffsetOf(authoring, authoring.chimneyTop, new float3(-0.92f, 5.4f, -0.39f)),
            });
            AddComponent(entity, new CaravanDriveTuning
            {
                maxForwardSpeed = authoring.maxForwardSpeed,
                maxReverseSpeed = authoring.maxReverseSpeed,
                acceleration = authoring.acceleration,
                brakeDeceleration = authoring.brakeDeceleration,
                coastDeceleration = authoring.coastDeceleration,
                turnDegreesPerSecondAtFullSpeed = authoring.turnDegreesPerSecondAtFullSpeed,
                turnDegreesPerSecondAtLowSpeed = authoring.turnDegreesPerSecondAtLowSpeed,
                parkMaxSpeed = authoring.parkMaxSpeed,
                wheelRadius = authoring.wheelRadius,
                frontBumperLocalZ = authoring.frontBumperLocalZ,
                rearBumperLocalZ = authoring.rearBumperLocalZ,
                halfWidth = authoring.halfWidth,
                bumperGroundClearance = authoring.bumperGroundClearance,
            });
            AddComponent(entity, new CaravanDriveState());

            AddComponent(entity, new CaravanDriving { driver = Entity.Null });
            SetComponentEnabled<CaravanDriving>(entity, false);
            // CaravanLinkSystem parks it once the owner exists; until then it is neither driving nor parked.
            AddComponent<CaravanParked>(entity);
            SetComponentEnabled<CaravanParked>(entity, false);

            AddComponent(entity, new CaravanDoors
            {
                phase = CaravanDoorPhase.Closed,
                openSeconds = authoring.doorOpenSeconds,
                closeSeconds = authoring.doorCloseSeconds,
                burstSeconds = authoring.doorBurstSeconds,
            });
            AddComponent(entity, new CaravanShake());
            AddComponent(entity, new CaravanPresentationEvents());

            AddComponent(entity, new CaravanCrashTuning
            {
                minimumImpactSpeed = authoring.minimumImpactSpeed,
                damagePerMetrePerSecond = authoring.damagePerMetrePerSecond,
                stunSecondsPerMetrePerSecond = authoring.stunSecondsPerMetrePerSecond,
                shoveDistance = authoring.shoveDistance,
                shoveSeconds = authoring.shoveSeconds,
                selfDamagePerHit = authoring.selfDamagePerHit,
                speedLossPerHit = authoring.speedLossPerHit,
                rehitCooldownSeconds = authoring.rehitCooldownSeconds,
            });
            AddBuffer<CaravanRecentHit>(entity);

            AddComponent(entity, new CaravanExitTuning
            {
                walkOutSeconds = authoring.walkOutSeconds,
                hopSeconds = authoring.hopSeconds,
                hopArcHeight = authoring.hopArcHeight,
                shakeBeforeBurstSeconds = authoring.shakeBeforeBurstSeconds,
                shakeBeforeBurstAmplitude = authoring.shakeBeforeBurstAmplitude,
                burstShakeSeconds = authoring.burstShakeSeconds,
                burstShakeAmplitude = authoring.burstShakeAmplitude,
                smokeCoverDelaySeconds = authoring.smokeCoverDelaySeconds,
                smokeLingerSeconds = authoring.smokeLingerSeconds,
            });
            AddComponent(entity, new CaravanExitSequence());
            SetComponentEnabled<CaravanExitSequence>(entity, false);

            AddComponent<CaravanChimneySmoking>(entity);
            SetComponentEnabled<CaravanChimneySmoking>(entity, false);
            AddComponent<CaravanBroken>(entity);
            SetComponentEnabled<CaravanBroken>(entity, false);

            // Reachable from anywhere along the body (CV-D16), not just near the origin by the back axle.
            AddComponent(entity, new PlayerInteractableReach
            {
                localSegmentStart = new float3(0f, 0f, authoring.rearBumperLocalZ + authoring.halfWidth),
                localSegmentEnd = new float3(0f, 0f, authoring.frontBumperLocalZ - authoring.halfWidth),
                radius = authoring.halfWidth,
            });

            // The caravan holds the ledger (CV-D5). Rows start empty; CaravanLinkSystem moves the player's stock in.
            AddBuffer<ResourceStack>(entity);
            AddBuffer<StorageCapacity>(entity);
            AddBuffer<ResourceChangeRequest>(entity);
            AddComponent(entity, new ResourceShortfall());
            SetComponentEnabled<ResourceShortfall>(entity, false);
        }

        private float3 LocalOffsetOf(CaravanAuthoring authoring, Transform point, float3 fallbackOffset)
        {
            if (point == null) return fallbackOffset;
            DependsOn(point);
            return authoring.transform.InverseTransformPoint(point.position);
        }
    }
}
