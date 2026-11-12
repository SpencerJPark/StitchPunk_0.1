using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public static class TerrainFeatureEvents
{
    public static event Action<Component> FeatureChanged;

    public static void Raise(Component feature)
    {
        if (Application.isPlaying)
        {
            return;
        }

        FeatureChanged?.Invoke(feature);
    }
}

// A closed spline outline the terrain baker fills with a recipe (forest, farmland, village ground).
[RequireComponent(typeof(SplineContainer))]
[ExecuteAlways]
public class TerrainArea : MonoBehaviour
{
    private const int MinimumKnotCount = 3;
    private const int InitialSamplesPerKnot = 4;
    private const int MaximumSubdivisionDepth = 12;
    private const float BoundsSampleSpacingMetres = 4f;

    public string featureId;
    public TerrainAreaRecipe recipe;
    public int seed;

    [Tooltip("Highest priority wins where areas overlap")]
    public int priority;

    [Min(0f)]
    public float edgeNoiseMetres = 3f;

    [Tooltip("-1 = chosen from seed")]
    public int cropIndex = -1;

    [Tooltip("Negative = along the longest edge")]
    public float rowDirectionDegrees = -1f;

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
        List<Vector3> worldPoints = SampleClosedOutlineWorld(BoundsSampleSpacingMetres);
        if (worldPoints.Count == 0)
        {
            return new Bounds(transform.position, Vector3.zero);
        }

        Bounds bounds = new Bounds(worldPoints[0], Vector3.zero);
        for (int pointIndex = 1; pointIndex < worldPoints.Count; pointIndex++)
        {
            bounds.Encapsulate(worldPoints[pointIndex]);
        }

        bounds.Expand(new Vector3(edgeNoiseMetres * 2f, 2f, edgeNoiseMetres * 2f));
        return bounds;
    }

    public List<Vector2> OutlineWorldXZ(float maxSegmentMetres)
    {
        List<Vector3> worldPoints = SampleClosedOutlineWorld(maxSegmentMetres);
        List<Vector2> outline = new List<Vector2>(worldPoints.Count);
        for (int pointIndex = 0; pointIndex < worldPoints.Count; pointIndex++)
        {
            outline.Add(new Vector2(worldPoints[pointIndex].x, worldPoints[pointIndex].z));
        }

        return outline;
    }

    // Samples a closed copy of the first spline so an open spline still yields a full loop; the first point is not repeated.
    private List<Vector3> SampleClosedOutlineWorld(float maxSegmentMetres)
    {
        List<Vector3> worldPoints = new List<Vector3>();
        SplineContainer container = Container;
        if (container == null || container.Splines.Count == 0 || container.Splines[0].Count < MinimumKnotCount)
        {
            return worldPoints;
        }

        Spline closedCopy = new Spline(container.Splines[0]);
        closedCopy.Closed = true;
        Matrix4x4 localToWorld = container.transform.localToWorldMatrix;
        float segmentLimit = Mathf.Max(0.01f, maxSegmentMetres);

        int initialSampleCount = closedCopy.Count * InitialSamplesPerKnot;
        Vector3 previousPoint = EvaluateWorld(closedCopy, localToWorld, 0f);
        float previousT = 0f;
        worldPoints.Add(previousPoint);
        for (int sampleIndex = 1; sampleIndex <= initialSampleCount; sampleIndex++)
        {
            float sampleT = sampleIndex / (float)initialSampleCount;
            Vector3 samplePoint = sampleIndex == initialSampleCount
                ? worldPoints[0]
                : EvaluateWorld(closedCopy, localToWorld, sampleT);
            SubdivideSegment(closedCopy, localToWorld, previousT, previousPoint, sampleT, samplePoint, segmentLimit, 0, worldPoints);
            if (sampleIndex < initialSampleCount)
            {
                worldPoints.Add(samplePoint);
            }

            previousT = sampleT;
            previousPoint = samplePoint;
        }

        return worldPoints;
    }

    private static void SubdivideSegment(
        Spline spline,
        Matrix4x4 localToWorld,
        float startT,
        Vector3 startPoint,
        float endT,
        Vector3 endPoint,
        float segmentLimit,
        int depth,
        List<Vector3> output)
    {
        if (depth >= MaximumSubdivisionDepth || Vector3.Distance(startPoint, endPoint) <= segmentLimit)
        {
            return;
        }

        float middleT = (startT + endT) * 0.5f;
        Vector3 middlePoint = EvaluateWorld(spline, localToWorld, middleT);
        SubdivideSegment(spline, localToWorld, startT, startPoint, middleT, middlePoint, segmentLimit, depth + 1, output);
        output.Add(middlePoint);
        SubdivideSegment(spline, localToWorld, middleT, middlePoint, endT, endPoint, segmentLimit, depth + 1, output);
    }

    private static Vector3 EvaluateWorld(Spline spline, Matrix4x4 localToWorld, float normalizedPosition)
    {
        Vector3 localPoint = SplineUtility.EvaluatePosition(spline, normalizedPosition);
        return localToWorld.MultiplyPoint3x4(localPoint);
    }

    private void Reset()
    {
        AssignFeatureIdIfEmpty();
    }

    private void OnEnable()
    {
        Spline.Changed += HandleSplineChanged;
        RememberTransform();
    }

    private void OnDisable()
    {
        Spline.Changed -= HandleSplineChanged;
        TerrainFeatureEvents.Raise(this);
    }

    private void OnValidate()
    {
        AssignFeatureIdIfEmpty();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                TerrainFeatureEvents.Raise(this);
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
            TerrainFeatureEvents.Raise(this);
        }
    }
#endif

    private void AssignFeatureIdIfEmpty()
    {
        if (string.IsNullOrEmpty(featureId))
        {
            featureId = Guid.NewGuid().ToString("N");
        }
    }

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
                TerrainFeatureEvents.Raise(this);
                return;
            }
        }
    }
}
