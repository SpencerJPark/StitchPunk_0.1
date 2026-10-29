using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// A box that opens its doors while an allowed opener stands in it, and lets them close when they leave
// (Caravan_System.md CV-D20). Not a physics trigger: DoorZoneSystem tests positions itself, so it never collides.
public class DoorZoneAuthoring : MonoBehaviour
{
    [Tooltip("Box centre and size in this object's local space (scale applies).")]
    public Vector3 center;
    public Vector3 size = new Vector3(2f, 2f, 2f);
    public DoorOpeners openers = DoorOpeners.Player;
    public DoorAuthoring[] doors;
    [Tooltip("While its vehicle is being driven this zone opens nothing, so nobody can get in or out.")]
    public bool lockedWhileDriven = true;
    [Tooltip("The vehicle that locks it. Empty = the nearest parent with a CaravanAuthoring, else the top of the hierarchy.")]
    public GameObject owner;

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.25f, 0.85f, 0.75f, 0.12f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0.25f, 0.85f, 0.75f, 0.9f);
        Gizmos.DrawWireCube(center, size);
    }

    public class Baker : Baker<DoorZoneAuthoring>
    {
        public override void Bake(DoorZoneAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Entity lockingVehicle = Entity.Null;
            if (authoring.lockedWhileDriven)
                lockingVehicle = GetEntity(DoorAuthoring.ResolveOwner(this, authoring.gameObject, authoring.owner), TransformUsageFlags.Dynamic);

            AddComponent(entity, new DoorZone
            {
                localCenter = authoring.center,
                halfExtents = math.abs((float3)authoring.size) * 0.5f,
                openers = authoring.openers,
                lockingVehicle = lockingVehicle,
            });

            DynamicBuffer<DoorZoneDoor> zoneDoors = AddBuffer<DoorZoneDoor>(entity);
            if (authoring.doors == null) return;
            foreach (DoorAuthoring door in authoring.doors)
            {
                if (door != null) zoneDoors.Add(new DoorZoneDoor { door = GetEntity(door, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
