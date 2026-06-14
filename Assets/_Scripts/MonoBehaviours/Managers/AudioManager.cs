using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Managed playback bridge for the Sound System ("ECS decides, MonoBehaviour plays"). Each LateUpdate
/// it writes the ListenerPosition + CameraView singletons from the active camera, reads the
/// ResolvedVoices the ECS mix produced, and drives a pool of AudioSources: one-shots fire on a free
/// voice, loops are kept alive by stableVoiceKey and stopped when they drop out of the list. It also
/// crossfades the music stems toward MusicState and applies the persisted GameSettings volumes to the
/// AudioMixer. Backend-agnostic — swap this layer for FMOD/Wwise without touching any ECS system.
/// </summary>
public class AudioManager : PersistentSingleton<AudioManager>
{
    [Header("Mixer")]
    public AudioMixer mixer;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup ambientGroup;
    public AudioMixerGroup musicGroup;

    [Header("Exposed mixer volume params (dB)")]
    public string masterParam  = "MasterVolume";
    public string musicParam   = "MusicVolume";
    public string sfxParam     = "SFXVolume";
    public string ambientParam = "AmbientVolume";

    [Header("Library + pool")]
    [Tooltip("Same SoundLibrarySO baked into the blob — used here for the managed AudioClip registry.")]
    public SoundLibrarySO soundLibrary;
    public int poolSize = 32;

    [Header("Listener / view")]
    [Tooltip("World radius around the camera ground point treated as 'on screen' for WorldMood + culling.")]
    public float cameraViewRadius = 25f;

    [Header("Music stems")]
    public AudioClip exploreMusic;
    public AudioClip tensionMusic;
    public AudioClip combatMusic;
    public float musicCrossfadeSpeed = 1.5f;

    private readonly Dictionary<SoundType, AudioClip[]> _clipsByType = new Dictionary<SoundType, AudioClip[]>();
    private readonly List<AudioSource> _pool = new List<AudioSource>();
    private readonly Dictionary<int, AudioSource> _loopVoices = new Dictionary<int, AudioSource>();
    private readonly HashSet<int> _seenLoopKeys = new HashSet<int>();
    private readonly List<int> _staleLoopKeys = new List<int>();
    private int _oneShotCursor;

    private AudioSource _exploreSource;
    private AudioSource _tensionSource;
    private AudioSource _combatSource;

    private EntityManager _entityManager;
    private bool _worldReady;
    private EntityQuery _resolvedQuery;
    private EntityQuery _listenerQuery;
    private EntityQuery _cameraViewQuery;
    private EntityQuery _musicQuery;
    private EntityQuery _settingsQuery;

    protected override void Awake()
    {
        base.Awake();
        BuildClipRegistry();
        BuildPool();
        BuildMusicSources();
    }

    private void BuildClipRegistry()
    {
        _clipsByType.Clear();
        if (soundLibrary == null) return;
        foreach (SoundSO so in soundLibrary.sounds)
        {
            if (so != null)
                _clipsByType[so.type] = so.clipVariations;
        }
    }

    private void BuildPool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = new GameObject($"Voice_{i}");
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f; // 3D
            source.rolloffMode = AudioRolloffMode.Linear;
            _pool.Add(source);
        }
    }

    private void BuildMusicSources()
    {
        _exploreSource = CreateMusicSource("Music_Explore", exploreMusic);
        _tensionSource = CreateMusicSource("Music_Tension", tensionMusic);
        _combatSource  = CreateMusicSource("Music_Combat", combatMusic);
    }

    private AudioSource CreateMusicSource(string label, AudioClip clip)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f; // 2D
        source.outputAudioMixerGroup = musicGroup;
        source.clip = clip;
        source.volume = 0f;
        if (clip != null) source.Play();
        return source;
    }

    private void LateUpdate()
    {
        if (!EnsureWorld()) return;

        WriteListenerAndView();
        ApplyMixerVolumes();
        UpdateMusic();
        UpdateVoices();
    }

    private bool EnsureWorld()
    {
        if (_worldReady) return true;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        _entityManager   = world.EntityManager;
        _resolvedQuery   = _entityManager.CreateEntityQuery(typeof(ResolvedVoices));
        _listenerQuery   = _entityManager.CreateEntityQuery(typeof(ListenerPosition));
        _cameraViewQuery = _entityManager.CreateEntityQuery(typeof(CameraView));
        _musicQuery      = _entityManager.CreateEntityQuery(typeof(MusicState));
        _settingsQuery   = _entityManager.CreateEntityQuery(typeof(GameSettings));
        _worldReady = true;
        return true;
    }

    // The AudioListener rides the active camera (assumed present on it). Here we publish the camera's
    // ground point + view radius so ECS culling/WorldMood agree with what the player sees.
    private void WriteListenerAndView()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 camPos = cam.transform.position;
        float3 ground = new float3(camPos.x, 0f, camPos.z);

        if (_listenerQuery.CalculateEntityCount() == 1)
            _listenerQuery.SetSingleton(new ListenerPosition { value = ground });

        if (_cameraViewQuery.CalculateEntityCount() == 1)
            _cameraViewQuery.SetSingleton(new CameraView { center = ground, viewRadius = cameraViewRadius });
    }

    private void ApplyMixerVolumes()
    {
        if (mixer == null || _settingsQuery.CalculateEntityCount() != 1) return;

        GameSettings settings = _settingsQuery.GetSingleton<GameSettings>();
        SetMixerDb(masterParam, settings.masterVolume);
        SetMixerDb(musicParam, settings.musicVolume);
        SetMixerDb(sfxParam, settings.sfxVolume);
        SetMixerDb(ambientParam, settings.ambientVolume);
    }

    private void SetMixerDb(string param, float linear)
    {
        if (string.IsNullOrEmpty(param)) return;
        float db = Mathf.Log10(Mathf.Clamp(linear, 0.0001f, 1f)) * 20f;
        mixer.SetFloat(param, db);
    }

    private void UpdateMusic()
    {
        if (_musicQuery.CalculateEntityCount() != 1) return;

        MusicState music = _musicQuery.GetSingleton<MusicState>();
        Crossfade(_exploreSource, music.exploreWeight);
        Crossfade(_tensionSource, music.tensionWeight);
        Crossfade(_combatSource, music.combatWeight);
    }

    private void Crossfade(AudioSource source, float target)
    {
        if (source == null) return;
        source.volume = Mathf.MoveTowards(source.volume, target, musicCrossfadeSpeed * Time.deltaTime);
        if (source.volume > 0f && source.clip != null && !source.isPlaying) source.Play();
    }

    private void UpdateVoices()
    {
        if (_resolvedQuery.CalculateEntityCount() != 1) return;

        ResolvedVoices resolved = _resolvedQuery.GetSingleton<ResolvedVoices>();
        if (!resolved.voices.IsCreated) return;

        NativeList<ResolvedVoice> voices = resolved.voices;
        _seenLoopKeys.Clear();

        for (int i = 0; i < voices.Length; i++)
        {
            ResolvedVoice voice = voices[i];
            if (voice.loop)
            {
                HandleLoop(voice);
                _seenLoopKeys.Add(voice.stableVoiceKey);
            }
            else
            {
                HandleOneShot(voice);
            }
        }

        // Stop loops that dropped out of the resolved set (component disabled / entity gone / culled).
        _staleLoopKeys.Clear();
        foreach (KeyValuePair<int, AudioSource> kv in _loopVoices)
        {
            if (!_seenLoopKeys.Contains(kv.Key))
            {
                kv.Value.Stop();
                kv.Value.clip = null;
                _staleLoopKeys.Add(kv.Key);
            }
        }
        for (int i = 0; i < _staleLoopKeys.Count; i++)
            _loopVoices.Remove(_staleLoopKeys[i]);
    }

    private void HandleOneShot(ResolvedVoice voice)
    {
        AudioClip clip = ResolveClip(voice);
        if (clip == null) return;

        AudioSource source = GetOneShotSource();
        if (source == null) return;

        source.transform.position = voice.pos;
        source.pitch = voice.pitch;
        source.spatialBlend = 1f;
        source.outputAudioMixerGroup = GroupFor(voice.bus);
        source.PlayOneShot(clip, Mathf.Clamp01(voice.volume));
    }

    private void HandleLoop(ResolvedVoice voice)
    {
        AudioClip clip = ResolveClip(voice);
        if (clip == null) return;

        if (!_loopVoices.TryGetValue(voice.stableVoiceKey, out AudioSource source))
        {
            source = GetFreeLoopSource();
            if (source == null) return;
            _loopVoices[voice.stableVoiceKey] = source;
            source.loop = true;
        }

        if (source.clip != clip) source.clip = clip;
        source.transform.position = voice.pos;
        source.pitch = voice.pitch;
        source.volume = Mathf.Clamp01(voice.volume);
        source.spatialBlend = 1f;
        source.outputAudioMixerGroup = GroupFor(voice.bus);
        if (!source.isPlaying) source.Play();
    }

    // Round-robins the pool for a one-shot voice, skipping sources currently held by a loop.
    private AudioSource GetOneShotSource()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            _oneShotCursor = (_oneShotCursor + 1) % _pool.Count;
            AudioSource candidate = _pool[_oneShotCursor];
            if (!IsLoopHeld(candidate)) return candidate;
        }
        return null;
    }

    private AudioSource GetFreeLoopSource()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            AudioSource candidate = _pool[i];
            if (!IsLoopHeld(candidate) && !candidate.isPlaying) return candidate;
        }
        return null;
    }

    private bool IsLoopHeld(AudioSource source)
    {
        foreach (AudioSource held in _loopVoices.Values)
            if (held == source) return true;
        return false;
    }

    private AudioClip ResolveClip(ResolvedVoice voice)
    {
        if (!_clipsByType.TryGetValue(voice.type, out AudioClip[] variations) || variations == null || variations.Length == 0)
            return null;
        int index = Mathf.Clamp(voice.variationIndex, 0, variations.Length - 1);
        return variations[index];
    }

    private AudioMixerGroup GroupFor(SoundBus bus)
    {
        switch (bus)
        {
            case SoundBus.Music:   return musicGroup;
            case SoundBus.Ambient: return ambientGroup;
            default:               return sfxGroup;
        }
    }
}
