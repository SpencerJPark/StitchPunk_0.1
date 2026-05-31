using System.Collections.Generic;
using UnityEngine;

// A unit archetype's action set. Baked into AISOBlob.brains, indexed by unitType.
[CreateAssetMenu(fileName = "BrainSO", menuName = "AI/Brain SO")]
public class BrainSO : ScriptableObject
{
    [Tooltip("Which UnitType this brain belongs to (blob slot index)")]
    public UnitType unitType;

    [Tooltip("Every action this unit can choose from")]
    public List<UtilityActionSO> actions = new List<UtilityActionSO>();
}
