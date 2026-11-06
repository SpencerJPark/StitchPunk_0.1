using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

public static class TerrainRoadEvents
{
    public static event Action<TerrainRoad> RoadChanged;

    public static void Raise(TerrainRoad road)
    {
        if (Application.isPlaying)
        {
            return;
        }

        RoadChanged?.Invoke(road);
    }
}

// A spline-authored road the terrain baker carves into the heightfield and paints onto the splat masks.
[RequireComponent(typeof(SplineContainer))]
[ExecuteAlways]
public class TerrainRoad : MonoBehaviour
{
    private const int BoundsSamplesPerSpline = 64;
    private const float FallbackHalfExtentMetres = 4f;

    public TerrainRoadProfile profile;

    [Tooltip("Highest priority wins where roads overlap")]
    public int priority;

    [Tooltip("Full road width in metres. 0 uses the profile's width.")]
    [Min(0f)]
    public float widthOverrideMetres;

    // Full width in metres; the one value the carve, the painted road and the ribbon all read.
    public float EffectiveWidthMetres
    {
        get
        {
            if (widthOverrideMetres > 0f) return widthOverrideMetres;
            return profile != null ? profile.widthMetres : FallbackHalfExtentMetres * 2f;
        }
    }

    public float EffectiveHalfWidthMetres => EffectiveWidthMetres * 0.5f;

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

        float horizontalExpand = profile != null
            ? EffectiveHalfWidthMetres + profile.carveShoulderMetres
            : FallbackHalfExtentMetres;
        bounds.Expand(new Vector3(horizontalExpand * 2f, 2f, horizontalExpand * 2f));
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
        TerrainRoadEvents.Raise(this);
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                TerrainRoadEvents.Raise(this);
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
            TerrainRoadEvents.Raise(this);
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
                TerrainRoadEvents.Raise(this);
                return;
            }
        }
    }
}
