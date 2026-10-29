using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// A door that DoorZoneAuthoring zones and scripts swing open (Caravan_System.md CV-D20). Put it on the door's
// hinge. With no clips, DoorSystem swings it about its parent's up axis; once the owner's actor has
// "<Animation Name>Open/Close/Slam" clips, those play instead (CV-D21) and the hinge must be a rig target.
public class DoorAuthoring : MonoBehaviour
{
    public string doorName = "Door";
    [Tooltip("Clip names are this + Open / Close / Slam, on the owner's actor. Empty = Door Name. Give two doors the same name to animate them as a pair.")]
    public string animationName;
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

            string animationName = string.IsNullOrEmpty(authoring.animationName) ? authoring.doorName : authoring.animationName;
            DoorAnimationKeys animationKeys = new DoorAnimationKeys
            {
                open = AnimationNameKeys.Resolve(AnimationNameConvention.ForDoorOpen(animationName)),
                close = AnimationNameKeys.Resolve(AnimationNameConvention.ForDoorClose(animationName)),
                slam = AnimationNameKeys.Resolve(AnimationNameConvention.ForDoorSlam(animationName)),
                lastAnimatedPhase = DoorPhase.Closed,
            };
            bool anyClip = animationKeys.open != 0 || animationKeys.close != 0 || animationKeys.slam != 0;
            if (!anyClip) return;
            if (animationKeys.open == 0 || animationKeys.close == 0 || animationKeys.slam == 0)
                Debug.LogWarning($"[DoorAuthoring] {authoring.name}: {animationName}Open/Close/Slam are only partly registered; the missing ones won't move the door.", authoring);
            AddComponent(entity, animationKeys);
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
