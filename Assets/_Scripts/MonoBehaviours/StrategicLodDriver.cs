using Unity.Entities;
using UnityEngine;

public class StrategicLodDriver : MonoBehaviour
{
    // The target already follows the eased zoom, so this only rounds off kinks; a real-time lock (cutscene) eases in more gently.
    private const float FollowSmoothingPerSecond = 6f;
    private const float LockedSmoothingPerSecond = 3f;

    public static bool ForceRealTime;
    public static float CurrentHudFade { get; private set; } = 1f;
    public static float CurrentScatterDistanceMultiplier { get; private set; } = 1f;

    private static int realTimeLockCount;

    [SerializeField] private StrategicZoomProfile zoomProfile;
    public float manualTimeScaleOverride;

    private World cachedWorld;
    private EntityQuery worldTimeScaleQuery;
    private bool ownsUnityTimeScale;

    public static void PushRealTimeLock()
    {
        realTimeLockCount++;
    }

    public static void PopRealTimeLock()
    {
        realTimeLockCount = Mathf.Max(0, realTimeLockCount - 1);
    }

    // Cutscenes and dialogue need 1x. Otherwise the speed is a pure function of the zoom amount, so it rises and falls exactly as
    // smoothly as the camera does (no snap to 1x when zooming back in).
    public static float ResolveTimeScaleTarget(float profileTarget, float manualOverride, bool realTimeLocked)
    {
        if (manualOverride > 0f)
        {
            return manualOverride;
        }
        if (realTimeLocked)
        {
            return 1f;
        }
        return profileTarget;
    }

    private void Awake()
    {
        if (zoomProfile == null)
        {
            zoomProfile = StrategicZoomProfile.CreateDefault();
        }
    }

    private void Update()
    {
        float mix01 = StrategicZoomState.Mix01;

        CurrentHudFade = zoomProfile.EvaluateHudFade(mix01);
        CurrentScatterDistanceMultiplier = zoomProfile.EvaluateScatterDistanceMultiplier(mix01);

        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld == null || !defaultWorld.IsCreated)
        {
            return;
        }
        if (cachedWorld != defaultWorld)
        {
            cachedWorld = defaultWorld;
            worldTimeScaleQuery = defaultWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<WorldTimeScale>());
        }
        if (worldTimeScaleQuery.CalculateEntityCount() != 1)
        {
            return;
        }

        bool realTimeLocked = ForceRealTime || realTimeLockCount > 0;
        WorldTimeScale worldTimeScale = worldTimeScaleQuery.GetSingleton<WorldTimeScale>();
        worldTimeScale.target = ResolveTimeScaleTarget(zoomProfile.EvaluateTimeScaleTarget(mix01), manualTimeScaleOverride, realTimeLocked);
        worldTimeScale.smoothingPerSecond = realTimeLocked ? LockedSmoothingPerSecond : FollowSmoothingPerSecond;
        worldTimeScaleQuery.SetSingleton(worldTimeScale);
        MirrorIntoUnityTimeScale(worldTimeScale.current);
    }

    // A paused game (timeScale 0) is never overridden; we only write while we own the value or the game is at normal speed.
    private void MirrorIntoUnityTimeScale(float currentScale)
    {
        bool atNormalSpeed = Mathf.Approximately(Time.timeScale, 1f);
        if (!ownsUnityTimeScale && !atNormalSpeed)
        {
            return;
        }
        if (Time.timeScale <= 0f)
        {
            ownsUnityTimeScale = false;
            return;
        }
        bool backToNormal = Mathf.Abs(currentScale - 1f) < 0.01f;
        Time.timeScale = backToNormal ? 1f : currentScale;
        ownsUnityTimeScale = !backToNormal;
    }

    private void OnDisable()
    {
        if (ownsUnityTimeScale && Time.timeScale > 0f)
        {
            Time.timeScale = 1f;
        }
        ownsUnityTimeScale = false;
    }
}
