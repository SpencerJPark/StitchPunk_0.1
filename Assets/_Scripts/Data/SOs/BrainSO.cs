using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Brain", menuName = "BrainSO/Brain")]
public class BrainSO : ScriptableObject
{
    [Header("Brain Data")]
    [SearchableEnum] public BrainType brainType;
    public FactionType factionType;
    public bool canBePlayerControlled = false;
    public float awarenessRange;

    [Header("Brain Behaviour")]
    [SearchableEnum] public BehaviourType[] behaviours;

    [Header("Random Brain Behaviour")] 
    public int amount;
    [SearchableEnum] public BehaviourType[] randomBehaviours;
    
    [Header("Agression Targets")]
    public FactionType[] attackFactions;
}

// States are mearly different brain swaps that happen when certain qualifiers are met