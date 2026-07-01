using Unity.Entities;
using UnityEngine;

/// <summary>
/// Bakes a Faction component and ThreatEntry buffer onto any body entity.
///
/// Add this to:
///   - Player entity        (FactionType.Player)
///   - Human NPC bodies     (FactionType.Human)
///   - Undead unit bodies   (FactionType.Undead)
///   - Any entity that can be targeted or tracked by combat AI
///
/// The ThreatEntry buffer is populated at runtime by DamageEventSystem whenever
/// this entity takes damage — attackers accumulate a threat score used to bias
/// target selection toward whoever is hurting the unit.
/// </summary>
public class FactionAuthoring : MonoBehaviour
{
    public FactionType factionType;

    public class Baker : Baker<FactionAuthoring>
    {
        public override void Bake(FactionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Faction { factionType = authoring.factionType });
            AddBuffer<ThreatEntry>(entity);
        }
    }
}
