using Unity.Entities;
using UnityEngine;

public class UnitBrainAuthoring : MonoBehaviour
{
    [SearchableEnum] public UnitType unitType;
    public UnitLibrarySO unitLibrary;
    public bool active = true;

    [Tooltip("Optional hand-socket child GO. Leave null if unit cannot hold items.")]
    public GameObject handSocket;

    public class Baker : Baker<UnitBrainAuthoring>
    {
        public override void Bake(UnitBrainAuthoring authoring)
        {
            if (authoring.unitLibrary == null) return;
            DependsOn(authoring.unitLibrary);

            UnitSO unitSO = authoring.unitLibrary.GetUnitSO(authoring.unitType);
            if (unitSO == null) return;

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            UnitBakingUtil.BakeRequirements(this, entity, authoring.active, unitSO);

            AddComponent(entity, new Faction { factionType = unitSO.factionType });
            AddBuffer<ThreatEntry>(entity);

            Entity socketEntity = authoring.handSocket != null
                ? GetEntity(authoring.handSocket, TransformUsageFlags.Dynamic)
                : Entity.Null;
            AddComponent(entity, new UnitEquip { socketEntity = socketEntity });
        }
    }
}
