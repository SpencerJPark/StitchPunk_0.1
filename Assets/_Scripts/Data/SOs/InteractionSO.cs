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

    [Tooltip("How long the interaction takes in seconds")]
    public float duration = 4f;
}

[Serializable]
public struct MotivationEntry
{
    [FormerlySerializedAs("motivationType")]
    [SearchableEnum] public NeedType needType;
    public int value;
}