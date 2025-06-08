using UnityEngine;

[CreateAssetMenu(fileName = "CharacterStateData", menuName = "Characters/Character State", order = 1)]
public class CharacterStateData : ScriptableObject
{
    [Header("Optional State Info")]
    [SerializeField] private string stateName = "Normal"; // e.g. "Sick", "Angry"
    public string StateName => stateName;


    [Header("Animation States")]
    [SerializeField] private string idleAnimation = "Idle";
    public string IdleAnimation => idleAnimation;
    
    [SerializeField] private string walkAnimation = "Walk";
    public string WalkAnimation => walkAnimation;

    [SerializeField] private string talkAnimation = "Talking";
    public string TalkAnimation => talkAnimation;

    
    [Header("Movement Speed")]
    [SerializeField] private float moveSpeed = 3f;
    public float MoveSpeed => moveSpeed;

    [SerializeField] private Color colorTint = Color.white; // Optional visual tint for the state
    public Color ColorTint => colorTint;

    // Future expansion
    // [SerializeField] private AudioClip footstepSound;
    // public AudioClip FootstepSound => footstepSound;

    // [SerializeField] private ParticleSystem effectOnEnter;
    // public ParticleSystem EffectOnEnter => effectOnEnter;
}
