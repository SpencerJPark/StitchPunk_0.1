using System.Collections.Generic;
using UnityEngine;

// The list of PartDefinitionSOs, one per PartDefId, baked into PartLibraryBlob by
// PartLibraryBakingSystem. Mirrors ItemLibrarySO. Drop the single _PartLibrary.asset onto a
// PartLibraryAuthoring GameObject in the scene.
[CreateAssetMenu(fileName = "_PartLibrary", menuName = "Units/Part Library")]
public class PartLibrarySO : ScriptableObject
{
    public List<PartDefinitionSO> parts = new();

    public PartDefinitionSO GetPart(PartDefId id)
    {
        foreach (PartDefinitionSO part in parts)
        {
            if (part != null && part.id == id)
                return part;
        }
        return null;
    }
}
