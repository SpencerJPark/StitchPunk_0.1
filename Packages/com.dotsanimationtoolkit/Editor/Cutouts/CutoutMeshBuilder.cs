// Copyright (c) 2026 Spencer Park. All rights reserved.
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static class CutoutMeshBuilder
    {
        private const float MaximumRoundedBendDegrees = 45f;

        public static bool TryBuild(IReadOnlyList<Vector2> outlinePixels, Vector2Int frameSize, Vector2 originPixels,
            float pixelsPerUnit, CutoutFacing facing, CutoutNormalMode normalMode, float roundness, Mesh target, out string failureReason)
        {
            return TryBuild(outlinePixels, frameSize, originPixels, pixelsPerUnit, facing, normalMode, roundness, null, target, out failureReason);
        }

        public static bool TryBuild(IReadOnlyList<Vector2> outlinePixels, Vector2Int frameSize, Vector2 originPixels,
            float pixelsPerUnit, CutoutFacing facing, CutoutNormalMode normalMode, float roundness,
            IReadOnlyList<Vector2Int> innerEdges, Mesh target, out string failureReason)
        {
            failureReason = string.Empty;
            if (outlinePixels == null || outlinePixels.Count < 3)
            {
                failureReason = "The outline needs at least three points.";
                return false;
            }
            if (frameSize.x <= 0 || frameSize.y <= 0)
            {
                failureReason = "The frame size must be larger than zero.";
                return false;
            }
            if (pixelsPerUnit <= 0f)
            {
                failureReason = "Pixels per unit must be larger than zero.";
                return false;
            }
            if (PolygonTriangulator.IsSelfIntersecting(outlinePixels))
            {
                failureReason = "The outline crosses itself — move a vertex so no edges cross.";
                return false;
            }
            List<int> triangles = new List<int>();
            List<int> skippedEdgeIndices = new List<int>();
            if (!PolygonTriangulator.TryTriangulateWithEdges(outlinePixels, innerEdges, triangles, skippedEdgeIndices))
            {
                failureReason = "The outline could not be triangulated — check for overlapping or duplicate points.";
                return false;
            }

            int vertexCount = outlinePixels.Count;
            Vector3[] positions = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            Vector3 positionSum = Vector3.zero;
            for (int index = 0; index < vertexCount; index++)
            {
                Vector2 pixel = outlinePixels[index];
                Vector2 unitPosition = (pixel - originPixels) / pixelsPerUnit;
                positions[index] = new Vector3(unitPosition.x, unitPosition.y, 0f);
                uvs[index] = new Vector2(pixel.x / frameSize.x, pixel.y / frameSize.y);
                positionSum += positions[index];
            }
            Vector3 centroid = positionSum / vertexCount;

            Vector3 facingNormal = facing == CutoutFacing.NegativeZ ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, 1f);
            Vector3[] normals = new Vector3[vertexCount];
            float bendTangent = Mathf.Tan(Mathf.Clamp01(roundness) * MaximumRoundedBendDegrees * Mathf.Deg2Rad);
            for (int index = 0; index < vertexCount; index++)
            {
                if (normalMode == CutoutNormalMode.Flat)
                {
                    normals[index] = facingNormal;
                    continue;
                }
                Vector3 outward = positions[index] - centroid;
                outward.z = 0f;
                outward = outward.sqrMagnitude > 0f ? outward.normalized : Vector3.zero;
                normals[index] = (facingNormal + outward * bendTangent).normalized;
            }

            // Ear clipping yields CCW in XY; Unity's -Z front face is clockwise in XY, so flip for NegativeZ.
            if (facing == CutoutFacing.NegativeZ)
            {
                for (int index = 0; index + 2 < triangles.Count; index += 3)
                {
                    int swapped = triangles[index + 1];
                    triangles[index + 1] = triangles[index + 2];
                    triangles[index + 2] = swapped;
                }
            }

            target.Clear();
            target.vertices = positions;
            target.uv = uvs;
            target.normals = normals;
            target.triangles = triangles.ToArray();
            target.RecalculateTangents();
            target.RecalculateBounds();
            return true;
        }

        public static float AreaFractionOfQuad(IReadOnlyList<Vector2> outlinePixels, Vector2Int frameSize)
        {
            if (outlinePixels == null || frameSize.x <= 0 || frameSize.y <= 0)
            {
                return 0f;
            }
            return Mathf.Abs(PolygonTriangulator.SignedArea(outlinePixels)) / ((float)frameSize.x * frameSize.y);
        }
    }
}
