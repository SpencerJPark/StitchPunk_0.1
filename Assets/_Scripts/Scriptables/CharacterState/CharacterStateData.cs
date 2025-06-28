using UnityEngine;

[CreateAssetMenu(fileName = "CharacterStateData", menuName = "Characters/Character State", order = 1)]
public class CharacterStateData : ScriptableObject
{
    [Header("Optional Visual State Info")]
    [SerializeField] private string stateName = "Normal";
    public string StateName => stateName;

    [Header("Animation States")]
    [SerializeField] private Actions idleAnimation = Actions.Idle;
    public Actions IdleAnimation => idleAnimation;

    [SerializeField] private Actions walkAnimation = Actions.Walk;
    public Actions WalkAnimation => walkAnimation;

    [SerializeField] private Actions talkAnimation = Actions.Talking;
    public Actions TalkAnimation => talkAnimation;

    [Header("Movement Speed")]
    [SerializeField] private float moveSpeed = 3f;
    public float MoveSpeed => moveSpeed;

    [SerializeField] private Color colorTint = Color.white;
    public Color ColorTint => colorTint;
}


    // Future expansion
    // [SerializeField] private AudioClip footstepSound;
    // public AudioClip FootstepSound => footstepSound;

    // [SerializeField] private ParticleSystem effectOnEnter;
    // public ParticleSystem EffectOnEnter => effectOnEnter;

