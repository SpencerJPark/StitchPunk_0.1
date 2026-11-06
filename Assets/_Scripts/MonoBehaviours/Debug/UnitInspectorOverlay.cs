#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.Rendering;

public static class UnitInspectorOverlay
{
    public static UnitInspectorSnapshot Snapshot { private get; set; }

    private const bool DrawOnTopOfGeometry = true;
    private const float LiftAboveGround = 0.15f;
    private const int CircleSegmentCount = 32;
    private const float DashSegmentLength = 0.5f;

    private static bool isInstalled;
    private static Material lineMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (isInstalled)
            return;
        isInstalled = true;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private static void EnsureMaterial()
    {
        if (lineMaterial != null)
            return;
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            return;
        lineMaterial = new Material(shader);
        lineMaterial.hideFlags = HideFlags.HideAndDontSave;
        lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        lineMaterial.SetInt("_Cull", (int)CullMode.Off);
        lineMaterial.SetInt("_ZWrite", 0);
        lineMaterial.SetInt("_ZTest", (int)(DrawOnTopOfGeometry ? CompareFunction.Always : CompareFunction.LessEqual));
    }

    private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
            return;
        UnitInspectorSnapshot snapshot = Snapshot;
        if (snapshot == null || !snapshot.hasUnit || !DebugMenuVisibility.IsToggleOn("Unit inspector"))
            return;
        EnsureMaterial();
        if (lineMaterial == null)
            return;

        GL.PushMatrix();
        // Camera matrices are not guaranteed to be loaded after URP's final blit.
        GL.LoadProjectionMatrix(GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
        GL.modelview = camera.worldToCameraMatrix;
        lineMaterial.SetPass(0);
        GL.Begin(GL.LINES);

        DrawRanges(snapshot);
        DrawPath(snapshot);
        DrawTargets(snapshot);
        DrawTrail(snapshot);

        GL.End();
        GL.PopMatrix();
    }

    private static void AddLine(Vector3 from, Vector3 to)
    {
        from.y += LiftAboveGround;
        to.y += LiftAboveGround;
        GL.Vertex(from);
        GL.Vertex(to);
    }

    private static void AddCross(Vector3 center, float size)
    {
        float half = size * 0.5f;
        AddLine(center + new Vector3(-half, 0f, 0f), center + new Vector3(half, 0f, 0f));
        AddLine(center + new Vector3(0f, 0f, -half), center + new Vector3(0f, 0f, half));
    }

    private static void AddCircle(Vector3 center, float radius)
    {
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int segmentIndex = 1; segmentIndex <= CircleSegmentCount; segmentIndex++)
        {
            float angle = segmentIndex * Mathf.PI * 2f / CircleSegmentCount;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            AddLine(previous, next);
            previous = next;
        }
    }

    private static void AddDiamond(Vector3 center, float size)
    {
        float half = size * 0.5f;
        Vector3 left = center + new Vector3(-half, 0f, 0f);
        Vector3 right = center + new Vector3(half, 0f, 0f);
        Vector3 back = center + new Vector3(0f, 0f, -half);
        Vector3 front = center + new Vector3(0f, 0f, half);
        AddLine(left, front);
        AddLine(front, right);
        AddLine(right, back);
        AddLine(back, left);
    }

    private static void AddDashedLine(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.0001f)
            return;
        Vector3 direction = delta / length;
        int segmentCount = Mathf.CeilToInt(length / DashSegmentLength);
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex += 2)
        {
            float startDistance = segmentIndex * DashSegmentLength;
            float endDistance = Mathf.Min(startDistance + DashSegmentLength, length);
            AddLine(from + direction * startDistance, from + direction * endDistance);
        }
    }

    private static void DrawRanges(UnitInspectorSnapshot snapshot)
    {
        if (snapshot.awarenessRange > 0f)
        {
            GL.Color(new Color(1f, 1f, 1f, 0.35f));
            AddCircle(snapshot.position, snapshot.awarenessRange);
        }
        if (snapshot.stoppingDistance > 0f)
        {
            GL.Color(new Color(1f, 0f, 1f, 0.8f));
            AddCircle(snapshot.position, snapshot.stoppingDistance);
        }
    }

    private static void DrawPath(UnitInspectorSnapshot snapshot)
    {
        if (snapshot.navState == UnitInspectorNavState.Abstract)
        {
            if (snapshot.hasAbstractRouteEnd)
            {
                GL.Color(new Color(0.2f, 0.4f, 1f, 0.9f));
                AddLine(snapshot.position, snapshot.abstractRouteEnd);
            }
            return;
        }

        int pointCount = snapshot.pathPoints.Count;
        if (pointCount == 0)
            return;

        Color pathColor = new Color(0.2f, 1f, 0.2f, 0.9f);
        if (snapshot.navState == UnitInspectorNavState.WaitingForPath)
            pathColor = new Color(1f, 0.9f, 0.1f, 0.9f);
        else if (snapshot.navState == UnitInspectorNavState.Stuck)
            pathColor = new Color(1f, 0.15f, 0.15f, 0.9f);

        GL.Color(pathColor);
        for (int pointIndex = 1; pointIndex < pointCount; pointIndex++)
            AddLine(snapshot.pathPoints[pointIndex - 1], snapshot.pathPoints[pointIndex]);

        if (snapshot.currentWaypointIndex >= 0 && snapshot.currentWaypointIndex < pointCount)
            AddCross(snapshot.pathPoints[snapshot.currentWaypointIndex], 0.5f);
        AddCross(snapshot.pathPoints[pointCount - 1], 1f);
    }

    private static void DrawTargets(UnitInspectorSnapshot snapshot)
    {
        if (snapshot.hasTargetEntity)
        {
            GL.Color(new Color(1f, 0.9f, 0.1f, 0.95f));
            DrawTargetMarker(snapshot.position, snapshot.targetEntityPosition);
        }
        if (snapshot.hasTargetPosition)
        {
            GL.Color(new Color(0f, 1f, 1f, 0.95f));
            DrawTargetMarker(snapshot.position, snapshot.targetPosition);
        }
    }

    private static void DrawTargetMarker(Vector3 unitPosition, Vector3 targetPosition)
    {
        AddLine(targetPosition, targetPosition + new Vector3(0f, 2f, 0f));
        AddDiamond(targetPosition + new Vector3(0f, 2f, 0f), 0.4f);
        AddDashedLine(unitPosition, targetPosition);
    }

    private static void DrawTrail(UnitInspectorSnapshot snapshot)
    {
        int pointCount = snapshot.trailPoints.Count;
        for (int pointIndex = 1; pointIndex < pointCount; pointIndex++)
        {
            float fade = pointCount > 2 ? (pointIndex - 1) / (float)(pointCount - 2) : 1f;
            GL.Color(new Color(1f, 1f, 1f, Mathf.Lerp(0.05f, 0.6f, fade)));
            AddLine(snapshot.trailPoints[pointIndex - 1], snapshot.trailPoints[pointIndex]);
        }
    }
}
#endif
