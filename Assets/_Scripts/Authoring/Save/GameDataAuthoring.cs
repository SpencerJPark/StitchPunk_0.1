using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place on one GO in the game scene. Bakes the GameData entity — the singleton that holds
/// save/load requests, auto-save timer, play time, and designer-tweakable game settings.
/// </summary>
public class GameDataAuthoring : MonoBehaviour
{
    [Header("Save")]
    [Tooltip("How often the game auto-saves, in seconds. Default: 300 (5 minutes).")]
    public float autoSaveIntervalSeconds = 300f;

    [Header("Settings")]
    [Range(1, 60)]
    [Tooltip("Flipbook animation playback rate shared across all animated units.")]
    public int animationFrameRate = 24;

    [Header("Audio (0–1, persisted)")]
    [Range(0f, 1f)] public float masterVolume  = 1f;
    [Range(0f, 1f)] public float musicVolume   = 0.7f;
    [Range(0f, 1f)] public float sfxVolume     = 0.9f;
    [Range(0f, 1f)] public float ambientVolume = 0.7f;

    public class Baker : Baker<GameDataAuthoring>
    {
        public override void Bake(GameDataAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new GameDataTag());

            AddComponent(entity, new SaveRequest());
            SetComponentEnabled<SaveRequest>(entity, false);

            AddComponent(entity, new LoadRequest());
            SetComponentEnabled<LoadRequest>(entity, false);

            AddComponent(entity, new AutoSaveTimer
            {
                elapsedSeconds  = 0f,
                intervalSeconds = authoring.autoSaveIntervalSeconds
            });

            AddComponent(entity, new PlayTimeTracker { totalSeconds = 0.0 });

            AddComponent(entity, new GameSettings
            {
                animationFrameRate = authoring.animationFrameRate,
                masterVolume       = authoring.masterVolume,
                musicVolume        = authoring.musicVolume,
                sfxVolume          = authoring.sfxVolume,
                ambientVolume      = authoring.ambientVolume,
            });

            // Dialogue persistent state
            AddBuffer<PlayedDialogue>(entity);
        }
    }
}
