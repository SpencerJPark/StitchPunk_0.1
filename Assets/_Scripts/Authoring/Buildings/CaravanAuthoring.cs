using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Goes beside WorkstationAuthoring on the caravan's simulation object (Caravan_System.md). The art lives
// in the scene on Caravan.prefab and follows this entity through CaravanPresenter.
[RequireComponent(typeof(WorkstationAuthoring))]
public class CaravanAuthoring : MonoBehaviour
{
    [Tooltip("How it drives, crashes and rides, and where the driver and horse sit per facing (CV-D18).")]
    public VehicleProfileSO profile;

    [Header("Points (empty = the default measured off Caravan.prefab)")]
    [Tooltip("Stand here and press E to drive. Parking puts the player back here.")]
    public Transform boardPoint;
    public Transform interiorStart;
    public Transform tailgateEdge;
    public Transform hopLanding;
    public Transform smokeBurstSpawn;
    public Transform chimneyTop;

    [Header("Doors")]
    [Tooltip("The doors a summon leaves through: held shut for a smoke burst, then slammed open, or held open for a walk-out.")]
    public DoorAuthoring[] summonExitDoors;

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
                boardPointLocalOffset = LocalOffsetOf(authoring, authoring.boardPoint, new float3(-2.6f, 0f, 3f)),
                interiorStartLocalOffset = LocalOffsetOf(authoring, authoring.interiorStart, new float3(0f, 1.93f, -0.6f)),
                tailgateEdgeLocalOffset = LocalOffsetOf(authoring, authoring.tailgateEdge, new float3(0f, 1.93f, -2.35f)),
                hopLandingLocalOffset = LocalOffsetOf(authoring, authoring.hopLanding, new float3(0f, 0f, -3.6f)),
                smokeBurstSpawnLocalOffset = LocalOffsetOf(authoring, authoring.smokeBurstSpawn, new float3(0f, 0f, -4f)),
                chimneyTopLocalOffset = LocalOffsetOf(authoring, authoring.chimneyTop, new float3(-0.92f, 5.4f, -0.39f)),
            });
            VehicleProfileSO profile = authoring.profile;
            if (profile != null) DependsOn(profile);
            else Debug.LogWarning($"[CaravanAuthoring] {authoring.name} has no VehicleProfileSO: it can't drive.", authoring);
            AddComponent(entity, new Vehicle { vehicleType = profile != null ? profile.vehicleType : VehicleType.None });
            AddComponent(entity, new CaravanDriveState());

            AddComponent(entity, new CaravanDriving { driver = Entity.Null });
            SetComponentEnabled<CaravanDriving>(entity, false);
            // CaravanLinkSystem parks it once the owner exists; until then it is neither driving nor parked.
            AddComponent<CaravanParked>(entity);
            SetComponentEnabled<CaravanParked>(entity, false);

            DynamicBuffer<CaravanSummonExitDoor> exitDoors = AddBuffer<CaravanSummonExitDoor>(entity);
            if (authoring.summonExitDoors != null)
            {
                foreach (DoorAuthoring door in authoring.summonExitDoors)
                {
                    if (door != null) exitDoors.Add(new CaravanSummonExitDoor { door = GetEntity(door, TransformUsageFlags.Dynamic) });
                }
            }
            AddComponent(entity, new CaravanShake());
            AddComponent(entity, new CaravanPresentationEvents());

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
            if (profile != null)
            {
                AddComponent(entity, new PlayerInteractableReach
                {
                    localSegmentStart = new float3(0f, 0f, profile.rearBumperLocalZ + profile.halfWidth),
                    localSegmentEnd = new float3(0f, 0f, profile.frontBumperLocalZ - profile.halfWidth),
                    radius = profile.halfWidth,
                });
            }

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
