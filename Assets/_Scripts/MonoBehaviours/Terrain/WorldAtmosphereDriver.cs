using System;
using Unity.Entities;
using UnityEngine;

public enum WeatherKind
{
    Clear,
    Cloudy,
    Storm,
}

// Time of day (from the shared WorldClock) and weather drive the sun, ambient, background, lightning and rain. Ground, props and the
// zoomed-out strategic layer all read the same light and ambient, so they change colour together.
public class WorldAtmosphereDriver : MonoBehaviour
{
    [Serializable]
    public struct WeatherLook
    {
        public float sunScale;
        public float ambientScale;
        [Range(0f, 1f)] public float saturation;
        public float rainParticlesPerSecond;
        public float lightningMinSeconds;
        public float lightningMaxSeconds;
    }

    private static readonly int GroundExposureBoostId = Shader.PropertyToID("_TerrainGroundExposureBoost");
    private const string DebugToggleName = "Atmosphere";
    private const float WeatherEasePerSecond = 0.4f;
    private const float SunriseHour = 6f;
    private const float SunsetHour = 18f;
    private const float MaxSunElevationDegrees = 62f;
    // Yaw is the direction the light travels: 315 = sun in the southeast, 405 = sun in the southwest.
    private const float SunriseLightYawDegrees = 315f;
    private const float SunsetLightYawDegrees = 405f;
    private const float HorizonSunElevationDegrees = 9f;
    private const float MoonElevationDegrees = 38f;
    private const float MoonYawDegrees = 205f;
    private const float FlashAmbientBoost = 0.3f;
    private const float RainBoxMetresAtGround = 70f;
    private const float RainBoxMetresZoomedOut = 900f;
    private const float RainFallMetresPerSecond = 45f;
    private const float RainLifetimeSeconds = 0.7f;
    private const float RainMaximumPerSecond = 9000f;

    [SerializeField] private Light sun;
    [SerializeField] private TerrainLightingBridge lightingBridge;
    [SerializeField] private Material rainMaterial;

    [Header("Time of day (0 = midnight)")]
    public Gradient sunColour = BuildSunColour();
    public AnimationCurve sunIntensity = BuildSunIntensity();
    public Gradient ambientColour = BuildAmbientColour();
    public Gradient backgroundColour = BuildBackgroundColour();
    [Tooltip("Ground-only brightness: the painted ground albedo is dark, so it is lifted without blowing out units and props.")]
    public float groundExposureBoost = 0.7f;

    [Header("Weather")]
    public WeatherKind weather = WeatherKind.Clear;
    public WeatherLook clearLook = new WeatherLook { sunScale = 1f, ambientScale = 1f, saturation = 1f };
    public WeatherLook cloudyLook = new WeatherLook { sunScale = 0.4f, ambientScale = 1.15f, saturation = 0.75f };
    public WeatherLook stormLook = new WeatherLook
    {
        sunScale = 0.12f, ambientScale = 0.7f, saturation = 0.55f,
        rainParticlesPerSecond = 6500f, lightningMinSeconds = 4f, lightningMaxSeconds = 11f,
    };
    public bool autoWeather = true;
    public Vector2 autoWeatherMinutesRange = new Vector2(5f, 12f);

    [Header("Debug override")]
    public bool useManualTimeOfDay;
    [Range(0f, 1f)] public float manualTimeOfDay01 = 0.7f;

    private WeatherLook blendedLook;
    private float secondsUntilLightning;
    private float secondsUntilSecondFlash = -1f;
    private float secondsUntilWeatherChange;
    private ParticleSystem rain;
    private Camera cachedCamera;
    private World cachedWorld;
    private EntityQuery clockQuery;

    private void Awake()
    {
        DebugMenuVisibility.RegisterToggle(DebugToggleName);
        blendedLook = LookFor(weather);
        secondsUntilWeatherChange = RollWeatherChangeSeconds();
        CreateRain();
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(GroundExposureBoostId, 0f);
    }

    private void Update()
    {
        DebugMenuVisibility.PollToggleAction();
        WeatherLook targetLook = LookFor(weather);
        blendedLook = Lerp(blendedLook, targetLook, 1f - Mathf.Exp(-WeatherEasePerSecond * Time.unscaledDeltaTime * 4f));

        TickAutoWeather();
        TickLightning(targetLook);
        ApplyLighting(ReadTimeOfDay01());
        UpdateRain();
    }

    public void SetWeather(WeatherKind kind)
    {
        weather = kind;
        secondsUntilLightning = 0.5f;
    }

    private WeatherLook LookFor(WeatherKind kind)
    {
        return kind == WeatherKind.Storm ? stormLook : kind == WeatherKind.Cloudy ? cloudyLook : clearLook;
    }

    private static WeatherLook Lerp(WeatherLook from, WeatherLook to, float amount)
    {
        return new WeatherLook
        {
            sunScale = Mathf.Lerp(from.sunScale, to.sunScale, amount),
            ambientScale = Mathf.Lerp(from.ambientScale, to.ambientScale, amount),
            saturation = Mathf.Lerp(from.saturation, to.saturation, amount),
            rainParticlesPerSecond = Mathf.Lerp(from.rainParticlesPerSecond, to.rainParticlesPerSecond, amount),
            lightningMinSeconds = to.lightningMinSeconds,
            lightningMaxSeconds = to.lightningMaxSeconds,
        };
    }

    private float ReadTimeOfDay01()
    {
        if (useManualTimeOfDay)
        {
            return manualTimeOfDay01;
        }
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld == null || !defaultWorld.IsCreated)
        {
            return manualTimeOfDay01;
        }
        if (cachedWorld != defaultWorld)
        {
            cachedWorld = defaultWorld;
            clockQuery = defaultWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldClock>());
        }
        return clockQuery.TryGetSingleton(out WorldClock clock) ? clock.timeOfDay01 : manualTimeOfDay01;
    }

    private void ApplyLighting(float timeOfDay01)
    {
        Shader.SetGlobalFloat(GroundExposureBoostId, groundExposureBoost);
        float flash = lightingBridge != null ? lightingBridge.CurrentFlash : 0f;
        float saturation = blendedLook.saturation;

        if (sun != null)
        {
            sun.color = Desaturate(sunColour.Evaluate(timeOfDay01), saturation);
            sun.intensity = sunIntensity.Evaluate(timeOfDay01) * blendedLook.sunScale;
            sun.transform.rotation = SunRotation(timeOfDay01);
        }

        Color ambient = Desaturate(ambientColour.Evaluate(timeOfDay01), saturation) * blendedLook.ambientScale;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = ambient + Color.white * (flash * FlashAmbientBoost);

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
        }
        bool clearsToBackgroundColour = cachedCamera != null && (cachedCamera.clearFlags == CameraClearFlags.SolidColor
            || (cachedCamera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox == null));
        if (clearsToBackgroundColour)
        {
            cachedCamera.backgroundColor = Desaturate(backgroundColour.Evaluate(timeOfDay01), saturation) * blendedLook.ambientScale;
        }
    }

    private static Quaternion SunRotation(float timeOfDay01)
    {
        float hour = timeOfDay01 * 24f;
        if (hour < SunriseHour || hour > SunsetHour)
        {
            return Quaternion.Euler(MoonElevationDegrees, MoonYawDegrees, 0f);
        }
        float dayFraction = (hour - SunriseHour) / (SunsetHour - SunriseHour);
        float elevation = Mathf.Max(HorizonSunElevationDegrees, Mathf.Sin(dayFraction * Mathf.PI) * MaxSunElevationDegrees);
        // The sun stays in the southern sky (the camera looks north), so the flat 2.5D characters are lit from the front all day
        // while shadows still swing from northwest to northeast.
        return Quaternion.Euler(elevation, Mathf.Lerp(SunriseLightYawDegrees, SunsetLightYawDegrees, dayFraction), 0f);
    }

    private static Color Desaturate(Color colour, float saturation)
    {
        float grey = colour.grayscale;
        return new Color(Mathf.Lerp(grey, colour.r, saturation), Mathf.Lerp(grey, colour.g, saturation), Mathf.Lerp(grey, colour.b, saturation), 1f);
    }

    private void TickAutoWeather()
    {
        if (!autoWeather)
        {
            return;
        }
        secondsUntilWeatherChange -= Time.unscaledDeltaTime;
        if (secondsUntilWeatherChange > 0f)
        {
            return;
        }
        secondsUntilWeatherChange = RollWeatherChangeSeconds();
        float roll = UnityEngine.Random.value;
        SetWeather(roll < 0.55f ? WeatherKind.Clear : roll < 0.85f ? WeatherKind.Cloudy : WeatherKind.Storm);
    }

    private float RollWeatherChangeSeconds()
    {
        return UnityEngine.Random.Range(autoWeatherMinutesRange.x, autoWeatherMinutesRange.y) * 60f;
    }

    private void TickLightning(WeatherLook targetLook)
    {
        if (lightingBridge == null || targetLook.lightningMaxSeconds <= 0f)
        {
            secondsUntilSecondFlash = -1f;
            return;
        }
        float delta = Time.unscaledDeltaTime;
        secondsUntilLightning -= delta;
        if (secondsUntilLightning <= 0f)
        {
            lightingBridge.TriggerLightning(UnityEngine.Random.Range(1.1f, 1.7f), 0.25f);
            secondsUntilSecondFlash = UnityEngine.Random.Range(0.12f, 0.25f);
            secondsUntilLightning = UnityEngine.Random.Range(targetLook.lightningMinSeconds, targetLook.lightningMaxSeconds);
        }
        if (secondsUntilSecondFlash >= 0f)
        {
            secondsUntilSecondFlash -= delta;
            if (secondsUntilSecondFlash < 0f)
            {
                lightingBridge.TriggerLightning(UnityEngine.Random.Range(0.6f, 1.1f), 0.2f);
            }
        }
    }

    private void CreateRain()
    {
        if (rainMaterial == null)
        {
            return;
        }
        GameObject rainObject = new GameObject("Rain");
        rainObject.transform.SetParent(transform, false);
        rain = rainObject.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = rain.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = RainLifetimeSeconds;
        main.startSpeed = 0f;
        main.maxParticles = 12000;

        ParticleSystem.ShapeModule shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;

        ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;

        ParticleSystemRenderer particleRenderer = rain.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.lengthScale = 3f;
        particleRenderer.velocityScale = 0.01f;
        particleRenderer.sharedMaterial = rainMaterial;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
    }

    private void UpdateRain()
    {
        if (rain == null)
        {
            return;
        }
        float zoom = StrategicZoomState.Mix01;
        float boxMetres = Mathf.Lerp(RainBoxMetresAtGround, RainBoxMetresZoomedOut, zoom);
        float scale = boxMetres / RainBoxMetresAtGround;
        float rate = Mathf.Min(blendedLook.rainParticlesPerSecond * Mathf.Sqrt(scale), RainMaximumPerSecond);

        Vector4 focus = Shader.GetGlobalVector("_TerrainFocusPosition");
        Vector3 focusPosition = focus.sqrMagnitude > 0f ? new Vector3(focus.x, focus.y, focus.z) : transform.position;
        float fallMetres = RainFallMetresPerSecond * scale * 0.5f;
        rain.transform.position = focusPosition + Vector3.up * (fallMetres * RainLifetimeSeconds);

        ParticleSystem.ShapeModule shape = rain.shape;
        shape.scale = new Vector3(boxMetres, 0.1f, boxMetres);
        ParticleSystem.VelocityOverLifetimeModule velocity = rain.velocityOverLifetime;
        velocity.y = -fallMetres;
        ParticleSystem.MainModule main = rain.main;
        main.startSize = Mathf.Lerp(0.03f, 0.16f, zoom);
        ParticleSystem.EmissionModule emission = rain.emission;
        emission.rateOverTime = rate;

        if (rate > 1f && !rain.isPlaying)
        {
            rain.Play();
        }
        else if (rate <= 1f && rain.isPlaying)
        {
            rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void OnGUI()
    {
        if (!DebugMenuVisibility.IsToggleOn(DebugToggleName))
        {
            return;
        }
        GUILayout.BeginArea(new Rect(10f, 730f, 420f, 150f), GUI.skin.box);
        GUILayout.Label("Atmosphere (debug)");
        GUILayout.BeginHorizontal();
        foreach (WeatherKind kind in (WeatherKind[])Enum.GetValues(typeof(WeatherKind)))
        {
            if (GUILayout.Button(kind.ToString()))
            {
                autoWeather = false;
                SetWeather(kind);
            }
        }
        GUILayout.EndHorizontal();
        autoWeather = GUILayout.Toggle(autoWeather, "Auto weather");
        useManualTimeOfDay = GUILayout.Toggle(useManualTimeOfDay, "Manual time of day");
        manualTimeOfDay01 = GUILayout.HorizontalSlider(manualTimeOfDay01, 0f, 1f);
        GUILayout.EndArea();
    }

    private static Gradient BuildSunColour()
    {
        return BuildGradient(
            (0.00f, new Color(0.40f, 0.50f, 1.00f)),
            (0.22f, new Color(0.40f, 0.50f, 1.00f)),
            (0.27f, new Color(1.00f, 0.72f, 0.50f)),
            (0.50f, new Color(1.00f, 0.97f, 0.88f)),
            (0.68f, new Color(1.00f, 0.96f, 0.82f)),
            (0.75f, new Color(1.00f, 0.80f, 0.52f)),
            (0.80f, new Color(0.66f, 0.42f, 0.55f)),
            (0.85f, new Color(0.40f, 0.50f, 1.00f)));
    }

    private static AnimationCurve BuildSunIntensity()
    {
        return new AnimationCurve(
            new Keyframe(0.00f, 0.5f), new Keyframe(0.22f, 0.5f), new Keyframe(0.27f, 0.75f),
            new Keyframe(0.35f, 1.25f), new Keyframe(0.50f, 1.4f), new Keyframe(0.68f, 1.35f),
            new Keyframe(0.75f, 1.0f), new Keyframe(0.80f, 0.4f), new Keyframe(0.85f, 0.5f), new Keyframe(1.00f, 0.5f));
    }

    private static Gradient BuildAmbientColour()
    {
        return BuildGradient(
            (0.00f, new Color(0.16f, 0.22f, 0.44f)),
            (0.22f, new Color(0.16f, 0.22f, 0.44f)),
            (0.27f, new Color(0.42f, 0.34f, 0.34f)),
            (0.50f, new Color(0.42f, 0.47f, 0.38f)),
            (0.68f, new Color(0.40f, 0.46f, 0.36f)),
            (0.75f, new Color(0.42f, 0.30f, 0.28f)),
            (0.80f, new Color(0.24f, 0.20f, 0.34f)),
            (0.85f, new Color(0.16f, 0.22f, 0.44f)));
    }

    private static Gradient BuildBackgroundColour()
    {
        return BuildGradient(
            (0.00f, new Color(0.05f, 0.08f, 0.20f)),
            (0.22f, new Color(0.05f, 0.08f, 0.20f)),
            (0.27f, new Color(0.62f, 0.50f, 0.44f)),
            (0.50f, new Color(0.60f, 0.70f, 0.60f)),
            (0.68f, new Color(0.66f, 0.68f, 0.52f)),
            (0.75f, new Color(0.70f, 0.50f, 0.38f)),
            (0.80f, new Color(0.30f, 0.26f, 0.36f)),
            (0.85f, new Color(0.05f, 0.08f, 0.20f)));
    }

    private static Gradient BuildGradient(params (float time, Color colour)[] keys)
    {
        GradientColorKey[] colourKeys = new GradientColorKey[keys.Length];
        for (int index = 0; index < keys.Length; index++)
        {
            colourKeys[index] = new GradientColorKey(keys[index].colour, keys[index].time);
        }
        Gradient gradient = new Gradient();
        gradient.SetKeys(colourKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }
}
