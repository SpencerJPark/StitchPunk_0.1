using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    /// <summary>One parent-to-child stage relationship drawn as an orthogonal polyline between two tile anchors.</summary>
    public readonly struct WorktreeEdge
    {
        public readonly Vector2 start;
        public readonly Vector2 end;
        public readonly WorktreeTileState childState;

        public WorktreeEdge(Vector2 start, Vector2 end, WorktreeTileState childState)
        {
            this.start = start;
            this.end = end;
            this.childState = childState;
        }
    }

    /// <summary>Absolute-filling layer behind the worktree tiles that paints skill-tree style right-angle edges between them.</summary>
    public sealed class WorktreeEdgeLayer : VisualElement
    {
        private const float EdgeLineWidth = 3f;
        private const float MinimumMidpointOffset = 16f;
        private const float ForkSquareHalfSize = 1.5f;

        private IReadOnlyList<WorktreeEdge> edges = System.Array.Empty<WorktreeEdge>();

        public WorktreeEdgeLayer()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += DrawEdges;
        }

        public void SetEdges(IReadOnlyList<WorktreeEdge> edges)
        {
            this.edges = edges ?? System.Array.Empty<WorktreeEdge>();
            MarkDirtyRepaint();
        }

        private void DrawEdges(MeshGenerationContext context)
        {
            if (edges.Count == 0)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            painter.lineWidth = EdgeLineWidth;
            painter.lineJoin = LineJoin.Miter;
            painter.lineCap = LineCap.Butt;

            // Locked edges first, then unlocked, then on-stage last, so the current path always sits on top.
            DrawEdgesForState(painter, WorktreeTileState.Locked, WorktreePalette.LineLocked);
            DrawEdgesForState(painter, WorktreeTileState.Unlocked, WorktreePalette.Teal);
            DrawEdgesForState(painter, WorktreeTileState.OnStage, WorktreePalette.Crimson);
        }

        private void DrawEdgesForState(Painter2D painter, WorktreeTileState state, Color color)
        {
            painter.strokeColor = color;
            painter.fillColor = color;

            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                WorktreeEdge edge = edges[edgeIndex];
                if (edge.childState != state)
                {
                    continue;
                }

                float midpointX = edge.start.x + Mathf.Max(MinimumMidpointOffset, (edge.end.x - edge.start.x) * 0.5f);
                Vector2 forkPoint = new Vector2(midpointX, edge.start.y);
                Vector2 descentPoint = new Vector2(midpointX, edge.end.y);

                painter.BeginPath();
                painter.MoveTo(edge.start);
                painter.LineTo(forkPoint);
                painter.LineTo(descentPoint);
                painter.LineTo(edge.end);
                painter.Stroke();

                DrawForkSquare(painter, forkPoint);
            }
        }

        private static void DrawForkSquare(Painter2D painter, Vector2 forkPoint)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(forkPoint.x - ForkSquareHalfSize, forkPoint.y - ForkSquareHalfSize));
            painter.LineTo(new Vector2(forkPoint.x + ForkSquareHalfSize, forkPoint.y - ForkSquareHalfSize));
            painter.LineTo(new Vector2(forkPoint.x + ForkSquareHalfSize, forkPoint.y + ForkSquareHalfSize));
            painter.LineTo(new Vector2(forkPoint.x - ForkSquareHalfSize, forkPoint.y + ForkSquareHalfSize));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
