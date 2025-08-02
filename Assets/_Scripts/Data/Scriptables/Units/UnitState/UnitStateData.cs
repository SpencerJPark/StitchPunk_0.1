using UnityEngine;
using Data;

// These are states that effect unit visual/movement data that can be applied to most units. Meant to be swapped at runtime

[CreateAssetMenu(fileName = "UnitStateData", menuName = "Units/Unit State", order = 2)]
public class UnitStateData : ScriptableObject
{
    [field: Header("Optional Visual State Info")]
    [field: SerializeField]
    public string StateName { get; private set; } = "Normal";

    [field: Header("Animation States")]
    [field: SerializeField]
    public ActionType IdleAnimation { get; private set; } = ActionType.Idle;

    [field: SerializeField]
    public ActionType WalkAnimation { get; private set; } = ActionType.Walk;

    [field: SerializeField]
    public ActionType TalkAnimation { get; private set; } = ActionType.Talking;
}



    // Future expansion
    // [SerializeField] private AudioClip footstepSound;
    // public AudioClip FootstepSound => footstepSound;

    // [SerializeField] private ParticleSystem effectOnEnter;
    // public ParticleSystem EffectOnEnter => effectOnEnter;

