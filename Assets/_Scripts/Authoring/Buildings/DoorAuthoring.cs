using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// A door that DoorZoneAuthoring zones and scripts swing open (Caravan_System.md CV-D20). Baked onto a real door
// mesh, DoorSystem swings this object about its parent's up axis. For hybrid art (the caravan), put it on the
// simulation object and give the art's hinge the same Door Name in CaravanPresenter.
public class DoorAuthoring : MonoBehaviour
{
    [Tooltip("Hybrid art finds its hinge by this name, so it must be unique among this owner's doors.")]
    public string doorName = "Door";
    [Tooltip("How far it swings open. The sign picks the direction.")]
    public float openDegrees = 110f;
    public float openSeconds = 0.6f;
    public float closeSeconds = 0.5f;
    [Tooltip("How long a slam open (summon smoke burst) takes, overshoot included.")]
    public float burstSeconds = 0.35f;
    [Tooltip("The vehicle or building this door belongs to. Empty = the nearest parent with a CaravanAuthoring, else the top of the hierarchy.")]
    public GameObject owner;

    public class Baker : Baker<DoorAuthoring>
    {
        public override void Bake(DoorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Door
            {
                phase = DoorPhase.Closed,
                openSeconds = authoring.openSeconds,
                closeSeconds = authoring.closeSeconds,
                burstSeconds = authoring.burstSeconds,
                openDegrees = authoring.openDegrees,
            });
            AddComponent(entity, new DoorName { value = new FixedString32Bytes(authoring.doorName ?? string.Empty) });
            GameObject ownerObject = ResolveOwner(this, authoring.gameObject, authoring.owner);
            AddComponent(entity, new DoorOwner { owner = GetEntity(ownerObject, TransformUsageFlags.Dynamic) });
            AddComponent(entity, new DoorHinge { restRotation = authoring.transform.localRotation });
        }
    }

    public static GameObject ResolveOwner(IBaker baker, GameObject authoringObject, GameObject explicitOwner)
    {
        if (explicitOwner != null) return explicitOwner;
        CaravanAuthoring caravan = baker.GetComponentInParent<CaravanAuthoring>(authoringObject);
        if (caravan != null) return caravan.gameObject;

        GameObject topmost = authoringObject;
        for (GameObject parent = baker.GetParent(topmost); parent != null; parent = baker.GetParent(topmost))
            topmost = parent;
        return topmost;
    }
}
