using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    /// <summary>One parent-to-child stage relationship drawn as a curve between two card anchors.</summary>
    public readonly struct WorktreeEdge
    {
        public readonly Vector2 start;
        public readonly Vector2 end;
        public readonly bool isActive;

        public WorktreeEdge(Vector2 start, Vector2 end, bool isActive)
        {
            this.start = start;
            this.end = end;
            this.isActive = isActive;
        }
    }

    /// <summary>Absolute-filling layer behind the worktree cards that paints Bezier edges between them.</summary>
    public sealed class WorktreeEdgeLayer : VisualElement
    {
        public static readonly Color ActiveEdgeColor = new Color(0.30f, 0.62f, 0.98f, 1f);
        public static readonly Color InactiveEdgeColor = new Color(0.30f, 0.62f, 0.98f, 0.35f);

        private const float EdgeLineWidth = 2f;

        private IReadOnlyList<WorktreeEdge> edges = System.Array.Empty<WorktreeEdge>();

        public WorktreeEdgeLayer()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += OnGenerateVisualContent;
        }

        public void SetEdges(IReadOnlyList<WorktreeEdge> edges)
        {
            this.edges = edges ?? System.Array.Empty<WorktreeEdge>();
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (edges.Count == 0)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            painter.lineWidth = EdgeLineWidth;

            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                WorktreeEdge edge = edges[edgeIndex];
                painter.strokeColor = edge.isActive ? ActiveEdgeColor : InactiveEdgeColor;

                // Horizontal-tangent cubic: control points sit halfway between the anchors on the x axis
                // so the curve leaves each card level and bends toward the other's row.
                float halfDistanceX = (edge.end.x - edge.start.x) * 0.5f;
                Vector2 startControlPoint = new Vector2(edge.start.x + halfDistanceX, edge.start.y);
                Vector2 endControlPoint = new Vector2(edge.end.x - halfDistanceX, edge.end.y);

                painter.BeginPath();
                painter.MoveTo(edge.start);
                painter.BezierCurveTo(startControlPoint, endControlPoint, edge.end);
                painter.Stroke();
            }
        }
    }
}
