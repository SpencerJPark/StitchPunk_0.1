using System.Collections.Generic;
using UnityEngine;

// One reusable ability, or a bundle of abilities, you add to a brain (AIFoundations AF-D2): "Throw Rock", "Camp Life".
// It wraps the scoring actions (each already points at its executed BehaviorSO) with a readable name,
// a description and tags, so a brain's module list reads like the unit's feature list.
[CreateAssetMenu(fileName = "BehaviourModule", menuName = "AI/Behaviour Module")]
public class BehaviourModuleSO : ScriptableObject
{
    public string displayName;

    [TextArea(2, 5)]
    public string description;

    [Tooltip("The scoring actions this module adds to a brain (each points at the BehaviorSO it runs). A module can bundle several, e.g. Camp Life = Sit + Eat + Music.")]
    public List<UtilityActionSO> actions = new List<UtilityActionSO>();

    [Tooltip("What the unit needs to use this module (hands, ranged weapon, needs, ...). Editor metadata for the future brain editor's filters; not read at bake or runtime.")]
    public ModuleTags requires;
}
