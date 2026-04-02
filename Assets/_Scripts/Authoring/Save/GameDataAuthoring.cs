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
                animationFrameRate = authoring.animationFrameRate
            });
        }
    }
}
