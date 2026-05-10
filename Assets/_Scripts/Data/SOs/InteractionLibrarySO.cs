using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_InteractionLibrary", menuName = "AI/Interaction Library")]
public class InteractionLibrarySO : ScriptableObject
{
    public List<InteractionSO> interactions = new List<InteractionSO>();

    public InteractionSO GetInteraction(ActionType type)
    {
        foreach (InteractionSO so in interactions)
        {
            if (so != null && so.interactionActionType == type)
                return so;
        }
        return null;
    }
}
