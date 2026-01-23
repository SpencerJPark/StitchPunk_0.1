using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class RandomTraitsAuthoring : MonoBehaviour
{
    [Range(1, 10)] public int traitCount = 3;
    public List<TraitFlags> traitPool = new List<TraitFlags>
    {
        TraitFlags.Smoker,
        TraitFlags.Drinker,
        TraitFlags.Workaholic,
        TraitFlags.Social,
        TraitFlags.Loner,
        TraitFlags.NightOwl,
        TraitFlags.EarlyBird,
        TraitFlags.Glutton,
    };

    public class Baker : Baker<RandomTraitsAuthoring>
    {
        public override void Bake(RandomTraitsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            if (authoring.traitPool == null || authoring.traitPool.Count == 0)
            {
                AddComponent<Traits>(entity, new Traits { flags = TraitFlags.None });
                return;
            }

            Unity.Mathematics.Random random = new Unity.Mathematics.Random((uint)(entity.GetHashCode() + 1));
            
            TraitFlags combinedFlags = TraitFlags.None;
            List<TraitFlags> availableTraits = new List<TraitFlags>(authoring.traitPool);
            int traitsToAssign = math.min(authoring.traitCount, availableTraits.Count);

            for (int i = 0; i < traitsToAssign; i++)
            {
                int index = random.NextInt(0, availableTraits.Count);
                TraitFlags selected = availableTraits[index];
                
                if (!HasConflict(combinedFlags, selected))
                {
                    combinedFlags |= selected;
                    AddTraitComponent(this, entity, selected);
                }
                
                availableTraits.RemoveAt(index);
            }

            AddComponent<Traits>(entity, new Traits { flags = combinedFlags });
        }

        private bool HasConflict(TraitFlags current, TraitFlags adding)
        {
            if ((adding == TraitFlags.Loner && (current & TraitFlags.Social) != 0) ||
                (adding == TraitFlags.Social && (current & TraitFlags.Loner) != 0))
                return true;

            if ((adding == TraitFlags.NightOwl && (current & TraitFlags.EarlyBird) != 0) ||
                (adding == TraitFlags.EarlyBird && (current & TraitFlags.NightOwl) != 0))
                return true;

            return false;
        }

        private void AddTraitComponent(Baker<RandomTraitsAuthoring> baker, Entity entity, TraitFlags trait)
        {
            switch (trait)
            {
                case TraitFlags.Smoker:
                    baker.AddComponent<CanSmoke>(entity);
                    break;
                case TraitFlags.Drinker:
                    baker.AddComponent<CanDrink>(entity);
                    break;
                case TraitFlags.Workaholic:
                    baker.AddComponent<IsWorkaholic>(entity);
                    break;
                case TraitFlags.Social:
                    baker.AddComponent<IsSocial>(entity);
                    break;
                case TraitFlags.Loner:
                    baker.AddComponent<IsLoner>(entity);
                    break;
                case TraitFlags.NightOwl:
                    baker.AddComponent<IsNightOwl>(entity);
                    break;
                case TraitFlags.EarlyBird:
                    baker.AddComponent<IsEarlyBird>(entity);
                    break;
                case TraitFlags.Glutton:
                    baker.AddComponent<IsGlutton>(entity);
                    break;
            }
        }
    }
}

[System.Flags]
public enum TraitFlags
{
    None = 0,
    Smoker = 1 << 0,
    Drinker = 1 << 1,
    Workaholic = 1 << 2,
    Social = 1 << 3,
    Loner = 1 << 4,
    NightOwl = 1 << 5,
    EarlyBird = 1 << 6,
    Glutton = 1 << 7,
}

public struct Traits : IComponentData
{
    public TraitFlags flags;
}