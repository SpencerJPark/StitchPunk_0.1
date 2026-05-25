using Unity.Entities;

public struct Personality : IComponentData
{
    public float bravery;        // [0, 1]: 0 = flees immediately, 1 = fights to the death
    public float socialAffinity; // [0, 1]: 0 = antisocial (Talk score × 0), 1 = extrovert (Talk score × 2)
    public float wanderlust;     // [0, 1]: 0 = stays put, 1 = always roaming (Wander/Movement score × 2)
    public float gluttony;       // [0, 1]: 0 = indifferent to food, 1 = food-obsessed (Hunger score × 2)
}
