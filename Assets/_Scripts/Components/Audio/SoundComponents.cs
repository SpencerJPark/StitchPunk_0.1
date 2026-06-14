using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// One-shot SFX request — spawned as a one-frame entity (the LogMessage lifecycle). Read by
// VoiceSelectionSystem, which feeds voice selection then DestroyEntity() the whole query.
public struct PlaySound : IComponentData
{
    public SoundType type;
    public Entity    source;         // emitter entity (Null if positional)
    public float3    position;       // captured world pos (used when !followSource)
    public bool      followSource;   // true → AudioSource tracks `source` each frame
    public float     volumeMul;      // 1 = use SoundSO default
    public float     pitchMul;       // 1 = use SoundSO default
    public SoundBus  busOverride;    // used only when hasBusOverride
    public bool      hasBusOverride; // false → SoundSO.bus
}

// Looping emitter — persistent on the world entity. AudioManager maps entity→voice; when this is
// disabled/removed or the entity is destroyed, the manager frees the voice (no explicit stop).
public struct LoopingSound : IComponentData, IEnableableComponent
{
    public SoundType type;
    public float     volumeMul;
    public float     pitchMul;
}

// Listener world position, written by AudioManager each frame from the active AudioListener/camera.
public struct ListenerPosition : IComponentData
{
    public float3 value;
}

// Camera ground-plane view, written by AudioManager. WorldMoodSystem uses it for "camera sees" tests.
public struct CameraView : IComponentData
{
    public float3 center;
    public float  viewRadius;
}

// One voice the manager should be playing this frame.
public struct ResolvedVoice
{
    public SoundType type;
    public int       variationIndex;
    public float3    pos;
    public Entity    source;
    public bool      followSource;
    public float     volume;
    public float     pitch;
    public bool      loop;
    public SoundBus  bus;
    public int       stableVoiceKey; // stable id so the manager can diff frame-to-frame
}

// Singleton holding the selected voices. The NativeList is owned by VoiceSelectionSystem
// (Allocator.Persistent, disposed in OnDestroy). AudioManager reads it each LateUpdate.
public struct ResolvedVoices : IComponentData
{
    public NativeList<ResolvedVoice> voices;
}

// Music layer target weights, derived from WorldMood by MusicStateSystem; read by AudioManager.
public struct MusicState : IComponentData
{
    public float exploreWeight;
    public float tensionWeight;
    public float combatWeight;
}
