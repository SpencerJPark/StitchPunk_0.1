using Unity.Entities;
using UnityEngine;

// Bakes the player-controllable minion components onto a unit body entity.
// Assign the selection ring child quad to selectedVisual — it must also have
// SelectionVisualAuthoring on it so SelectionColor gets baked onto that entity.
//
// Add UndeadAuthoring separately on zombie prefabs that can also be revived.
public class MinionAuthoring : MonoBehaviour
{
    public bool startMinion;

    [Tooltip("Child quad that shows the selection ring. Also receives SelectionColor for shader tinting.")]
    public GameObject selectedVisual;
    public float showScale;

    public class Baker : Baker<MinionAuthoring>
    {
        public override void Bake(MinionAuthoring authoring)
        {
            Entity bodyEntity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<Minion>(bodyEntity);
            SetComponentEnabled<Minion>(bodyEntity, authoring.startMinion);

            Entity visualEntity = authoring.selectedVisual != null
                ? GetEntity(authoring.selectedVisual, TransformUsageFlags.Dynamic)
                : Entity.Null;

            AddComponent(bodyEntity, new Selected
            {
                visualEntity = visualEntity,
                showScale    = authoring.showScale,
            });
            SetComponentEnabled<Selected>(bodyEntity, false);

            // Squad 0 = unassigned; set by the number keys or by joining the active squad on summon.
            AddComponent(bodyEntity, new SquadMember { squadIndex = 0 });
            AddComponent(bodyEntity, new MinionStandingOrder { type = StandingOrderType.None });
            AddComponent(bodyEntity, new MinionGatherOrder { phase = GatherPhase.None });
        }
    }
}
