using Unity.Entities;

public static class BrainBlobUtils
{
    // Returns the actionDefs index for the first action matching actionType in this unit's brain.
    // Returns -1 if the brain has no entry for that UnitType or no matching action.
    public static int GetActionDefIndex(
        ref BrainLibraryBlob blob,
        UnitType             unitType,
        ActionType           actionType)
    {
        int brainIndex = (int)unitType;
        if (brainIndex < 0 || brainIndex >= blob.brains.Length)
            return -1;

        ref BrainBlob brain = ref blob.brains[brainIndex];
        for (int i = 0; i < brain.actionDefIndices.Length; i++)
        {
            int defIndex = brain.actionDefIndices[i];
            if (blob.actionDefs[defIndex].actionType == actionType)
                return defIndex;
        }
        return -1;
    }
}
