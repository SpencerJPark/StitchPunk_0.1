using UnityEngine;

// These are states that effect unit visual/movement data that can be applied to most units. Meant to be swapped at runtime

[CreateAssetMenu(fileName = "UnitStateData", menuName = "Units/Unit State", order = 2)]
public class UnitStateData : ScriptableObject
{
    [field: Header("Optional Visual State Info")]
    [field: SerializeField]
    public string StateName { get; private set; } = "Normal";

    [field: Header("Animation States")]
    [field: SerializeField]
    public Actions IdleAnimation { get; private set; } = Actions.Idle;

    [field: SerializeField]
    public Actions WalkAnimation { get; private set; } = Actions.Walk;

    [field: SerializeField]
    public Actions TalkAnimation { get; private set; } = Actions.Talking;
}



    // Future expansion
    // [SerializeField] private AudioClip footstepSound;
    // public AudioClip FootstepSound => footstepSound;

    // [SerializeField] private ParticleSystem effectOnEnter;
    // public ParticleSystem EffectOnEnter => effectOnEnter;

