using UnityEngine;

// One reusable ability you add to a brain (SmartHumanAI H0): "Throw Rock", "Listen to Music", "Eat".
// It wraps the scoring action (which already points at its executed BehaviorSO) with a readable name,
// a description and tags, so a brain's module list reads like the unit's feature list.
[CreateAssetMenu(fileName = "BehaviourModule", menuName = "AI/Behaviour Module")]
public class BehaviourModuleSO : ScriptableObject
{
    public string displayName;

    [TextArea(2, 5)]
    public string description;

    [Tooltip("The scoring action this module adds to a brain; its BehaviorSO is what runs.")]
    public UtilityActionSO action;

    [Tooltip("What the unit needs to use this module (hands, ranged weapon, needs, ...).")]
    public ModuleTags requires;
}
