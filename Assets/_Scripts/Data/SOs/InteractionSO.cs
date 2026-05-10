using System;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Interaction", menuName = "AI/Interaction")]
public class InteractionSO : ScriptableObject
{
    [SearchableEnum] public ActionType interactionActionType;
    
    public int priority;

    public int maxOccupants;
    
    [Header("Motivation Satisfaction")]
    [Tooltip("Which NPC motivations this interaction satisfies and by how much (flat units, 0–100). " +
             "An entry with value 0 is skipped.")]
    public MotivationEntry motivationSatisfaction;
    
    [SearchableEnum] public FactionType[] factionsThatCanInteractWith;

    [Tooltip("How close to perform interaction")]
    public float range;
}

[Serializable]
public struct MotivationEntry
{
    [SearchableEnum] public MotivationType motivationType;
    public int value;
}