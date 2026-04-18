using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "_BrainLibrary", menuName = "BrainSO/Library")]
public class BrainLibrarySO : ScriptableObject
{
    public List<BrainSO> brains = new List<BrainSO>();

    public BrainSO GetBrain(BrainType brainType)
    {
        foreach (var brain in brains)
        {
            if (brain != null && brain.brainType == brainType)
                return brain;
        }
        return null;
    }
}