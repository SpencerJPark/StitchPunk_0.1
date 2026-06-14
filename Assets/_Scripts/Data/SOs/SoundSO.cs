using UnityEngine;

[CreateAssetMenu(fileName = "Sound", menuName = "Audio/Sound")]
public class SoundSO : ScriptableObject
{
    [SearchableEnum] public SoundType type;
    [SearchableEnum] public SoundBus  bus = SoundBus.SFX;

    [Header("Clips")]
    [Tooltip("Random pick per play for anti-repetition. Managed clips — held by AudioManager, never baked.")]
    public AudioClip[] clipVariations;

    [Header("Randomization (per play)")]
    [Tooltip("Random volume multiplier range.")]
    public Vector2 volumeRange = new Vector2(1f, 1f);
    [Tooltip("Random pitch multiplier range — break up repetition (e.g. 0.95–1.05).")]
    public Vector2 pitchRange = new Vector2(1f, 1f);

    [Header("Playback")]
    public bool loop;
    [Range(0, 255)]
    [Tooltip("Higher = harder to steal when the voice pool is full.")]
    public int priority = 128;
    [Tooltip("3D rolloff near distance.")]
    public float minDistance = 1f;
    [Tooltip("3D rolloff far distance — widen for god-mode-audible sounds.")]
    public float maxDistance = 30f;
    [Tooltip("Cap on simultaneous instances of this type.")]
    public int maxConcurrent = 8;
    [Tooltip("Seconds; same-type emits inside this window collapse into one.")]
    public float dedupWindow = 0.05f;
}
