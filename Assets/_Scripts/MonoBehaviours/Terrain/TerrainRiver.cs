using System;
using UnityEngine;
using UnityEngine.Splines;

public static class TerrainWaterEvents
{
    public static event Action<TerrainRiver> RiverChanged;
    public static event Action<TerrainWaterBody> BodyChanged;
    public static event Action<TerrainCrossing> CrossingChanged;

    public static void RaiseRiver(TerrainRiver river)
    {
        if (Application.isPlaying)
        {
            return;
        }

        RiverChanged?.Invoke(river);
    }

    public static void RaiseBody(TerrainWaterBody body)
    {
        if (Application.isPlaying)
        {
            return;
        }

        BodyChanged?.Invoke(body);
    }

    public static void RaiseCrossing(TerrainCrossing crossing)
    {
        if (Application.isPlaying)
        {
            return;
        }

        CrossingChanged?.Invoke(crossing);
    }
}

// A spline-authored river the terrain baker carves a channel for and fills with water.
[RequireComponent(typeof(SplineContainer))]
[ExecuteAlways]
public class TerrainRiver : MonoBehaviour
{
    private const int BoundsSamplesPerSpline = 64;
    private const float FallbackHalfExtentMetres = 20f;

    public TerrainWaterProfile profile;

    [Tooltip("Water surface height at the river start; NaN starts at the terrain surface")]
    public float startWaterLevelMetres = float.NaN;

    [Tooltip("Highest priority wins where rivers overlap")]
    public int priority;

    private SplineContainer cachedContainer;
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 lastScale;

    public SplineContainer Container
    {
        get
        {
            if (cachedContainer == null)
            {
                cachedContainer = GetComponent<SplineContainer>();
            }

            return cachedContainer;
        }
    }

    public Bounds WorldBounds()
    {
        SplineContainer container = Container;
        if (container == null || container.Splines.Count == 0)
        {
            return new Bounds(transform.position, Vector3.zero);
        }

        Bounds bounds = new Bounds();
        bool hasPoint = false;
        for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
        {
            for (int step = 0; step <= BoundsSamplesPerSpline; step++)
            {
                float normalizedPosition = step / (float)BoundsSamplesPerSpline;
                // SplineContainer.EvaluatePosition already returns world space
                Vector3 worldPoint = container.EvaluatePosition(splineIndex, normalizedPosition);
                if (!hasPoint)
                {
                    bounds = new Bounds(worldPoint, Vector3.zero);
                    hasPoint = true;
                }
                else
                {
                    bounds.Encapsulate(worldPoint);
                }
            }
        }

        float horizontalExpand = FallbackHalfExtentMetres;
        if (profile != null)
        {
            float maximumWidthMetres = Mathf.Max(profile.WidthAt(0f), profile.WidthAt(1f));
            for (int step = 0; step <= BoundsSamplesPerSpline; step++)
            {
                maximumWidthMetres = Mathf.Max(maximumWidthMetres, profile.WidthAt(step / (float)BoundsSamplesPerSpline));
            }

            horizontalExpand = maximumWidthMetres * 0.5f + profile.bankSlopeMetres;
        }

        // Bounds.Expand adds the amount to the full size, so double it for a per-side margin
        bounds.Expand(new Vector3(horizontalExpand * 2f, 3f * 2f, horizontalExpand * 2f));
        return bounds;
    }

    private void OnEnable()
    {
        Spline.Changed += HandleSplineChanged;
        RememberTransform();
    }

    private void OnDisable()
    {
        Spline.Changed -= HandleSplineChanged;
        TerrainWaterEvents.RaiseRiver(this);
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                TerrainWaterEvents.RaiseRiver(this);
            }
        };
#endif
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (transform.position != lastPosition
            || transform.rotation != lastRotation
            || transform.lossyScale != lastScale)
        {
            RememberTransform();
            TerrainWaterEvents.RaiseRiver(this);
        }
    }
#endif

    private void RememberTransform()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        lastScale = transform.lossyScale;
    }

    private void HandleSplineChanged(Spline changedSpline, int knotIndex, SplineModification modification)
    {
        SplineContainer container = Container;
        if (container == null)
        {
            return;
        }

        for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
        {
            if (container.Splines[splineIndex] == changedSpline)
            {
                TerrainWaterEvents.RaiseRiver(this);
                return;
            }
        }
    }
}
