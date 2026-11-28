using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

// An open (or closed-loop) spline the terrain baker lines with placements (fence posts, lamp rows); writes no area mask.
[RequireComponent(typeof(SplineContainer))]
[ExecuteAlways]
public class TerrainPath : MonoBehaviour
{
    private const int MinimumOpenKnotCount = 2;
    private const int MinimumClosedKnotCount = 3;
    private const int InitialSamplesPerSegment = 4;
    private const int MaximumSubdivisionDepth = 12;
    private const float BoundsSampleSpacingMetres = 4f;
    private const float BoundsMarginMetres = 2f;

    public string featureId;
    public TerrainFeatureRecipe recipe;
    public int seed;

    [Tooltip("Writes no area mask, only placements")]
    public bool closedLoop;

    [Tooltip("A gap knot skips the segment that starts there (fence gates)")]
    public List<int> gapKnotIndices = new List<int>();

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
        List<Vector3> worldPoints = SamplePathWorld(BoundsSampleSpacingMetres);
        if (worldPoints.Count == 0)
        {
            return new Bounds(transform.position, Vector3.zero);
        }

        Bounds bounds = new Bounds(worldPoints[0], Vector3.zero);
        for (int pointIndex = 1; pointIndex < worldPoints.Count; pointIndex++)
        {
            bounds.Encapsulate(worldPoints[pointIndex]);
        }

        bounds.Expand(new Vector3(BoundsMarginMetres * 2f, 2f, BoundsMarginMetres * 2f));
        return bounds;
    }

    public List<Vector2> PolylineWorldXZ(float maxSegmentMetres)
    {
        List<Vector3> worldPoints = SamplePathWorld(maxSegmentMetres);
        List<Vector2> polyline = new List<Vector2>(worldPoints.Count);
        for (int pointIndex = 0; pointIndex < worldPoints.Count; pointIndex++)
        {
            polyline.Add(new Vector2(worldPoints[pointIndex].x, worldPoints[pointIndex].z));
        }

        return polyline;
    }

    // Samples a copy of the first spline forced to match closedLoop; a closed loop does not repeat its first point.
    private List<Vector3> SamplePathWorld(float maxSegmentMetres)
    {
        List<Vector3> worldPoints = new List<Vector3>();
        SplineContainer container = Container;
        int minimumKnotCount = closedLoop ? MinimumClosedKnotCount : MinimumOpenKnotCount;
        if (container == null || container.Splines.Count == 0 || container.Splines[0].Count < minimumKnotCount)
        {
            return worldPoints;
        }

        Spline pathCopy = new Spline(container.Splines[0]);
        pathCopy.Closed = closedLoop;
        Matrix4x4 localToWorld = container.transform.localToWorldMatrix;
        float segmentLimit = Mathf.Max(0.01f, maxSegmentMetres);

        int segmentCount = closedLoop ? pathCopy.Count : pathCopy.Count - 1;
        int initialSampleCount = segmentCount * InitialSamplesPerSegment;
        Vector3 previousPoint = EvaluateWorld(pathCopy, localToWorld, 0f);
        float previousT = 0f;
        worldPoints.Add(previousPoint);
        for (int sampleIndex = 1; sampleIndex <= initialSampleCount; sampleIndex++)
        {
            bool isLoopClosingSample = closedLoop && sampleIndex == initialSampleCount;
            float sampleT = sampleIndex / (float)initialSampleCount;
            Vector3 samplePoint = isLoopClosingSample
                ? worldPoints[0]
                : EvaluateWorld(pathCopy, localToWorld, sampleT);
            SubdivideSegment(pathCopy, localToWorld, previousT, previousPoint, sampleT, samplePoint, segmentLimit, 0, worldPoints);
            if (!isLoopClosingSample)
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
        TerrainRebakeGate.RememberSerializedState(this);
        Spline.Changed += HandleSplineChanged;
        RememberTransform();
    }

    private void OnDisable()
    {
        Spline.Changed -= HandleSplineChanged;
    }

    private void OnValidate()
    {
        AssignFeatureIdIfEmpty();
        SynchroniseSplineClosedFlag();
#if UNITY_EDITOR
        if (!TerrainRebakeGate.ShouldRaiseForValidate(this))
        {
            return;
        }

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && !TerrainRebakeGate.SuppressRebakeEvents)
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

    private void SynchroniseSplineClosedFlag()
    {
        SplineContainer container = Container;
        if (container == null || container.Splines.Count == 0)
        {
            return;
        }

        if (container.Splines[0].Closed != closedLoop)
        {
            container.Splines[0].Closed = closedLoop;
        }
    }

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
        if (TerrainRebakeGate.SuppressRebakeEvents)
        {
            return;
        }

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
