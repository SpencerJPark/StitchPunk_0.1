#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct TerrainCard
{
    public Vector3 basePosition;
    public Vector2 sizeMetres;
    public Rect uvRect;
    public float tint01;
}

public static class TerrainCardBatchMeshBuilder
{
    public const int MaxCardsPerMesh = 16000;

    private const int VerticesPerCard = 4;
    private const int IndicesPerCard = 6;
    private const int SixteenBitVertexLimit = 65535;

    // Quads stand on basePosition, upright (world up), facing cameraYawDegrees. Vertex colour r = tint01.
    // uv2.y = 0 at the base, 1 at the top (for wind and ground blend).
    public static Mesh Build(IReadOnlyList<TerrainCard> cards, Vector3 meshOriginWorld, float cameraYawDegrees, out int vertexCount)
    {
        if (cards.Count > MaxCardsPerMesh)
        {
            throw new ArgumentException("Card count " + cards.Count + " exceeds MaxCardsPerMesh " + MaxCardsPerMesh + "; split the batch in the caller.", nameof(cards));
        }

        vertexCount = cards.Count * VerticesPerCard;
        Mesh mesh = new Mesh();
        mesh.name = "TerrainCardBatch";
        if (vertexCount == 0)
        {
            return mesh;
        }

        float yawRadians = cameraYawDegrees * Mathf.Deg2Rad;
        float sinYaw = Mathf.Sin(yawRadians);
        float cosYaw = Mathf.Cos(yawRadians);
        Vector3 widthAxis = new Vector3(cosYaw, 0f, -sinYaw);
        Vector3 faceNormal = new Vector3(-sinYaw, 0f, -cosYaw);

        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvZero = new Vector2[vertexCount];
        Vector2[] uvTwo = new Vector2[vertexCount];
        Color[] colors = new Color[vertexCount];
        int[] indices = new int[cards.Count * IndicesPerCard];

        for (int cardIndex = 0; cardIndex < cards.Count; cardIndex++)
        {
            TerrainCard card = cards[cardIndex];
            Vector3 localBase = card.basePosition - meshOriginWorld;
            Vector3 halfWidth = widthAxis * (card.sizeMetres.x * 0.5f);
            Vector3 heightOffset = Vector3.up * card.sizeMetres.y;
            int firstVertex = cardIndex * VerticesPerCard;

            vertices[firstVertex] = localBase - halfWidth;
            vertices[firstVertex + 1] = localBase + halfWidth;
            vertices[firstVertex + 2] = localBase - halfWidth + heightOffset;
            vertices[firstVertex + 3] = localBase + halfWidth + heightOffset;

            uvZero[firstVertex] = new Vector2(card.uvRect.xMin, card.uvRect.yMin);
            uvZero[firstVertex + 1] = new Vector2(card.uvRect.xMax, card.uvRect.yMin);
            uvZero[firstVertex + 2] = new Vector2(card.uvRect.xMin, card.uvRect.yMax);
            uvZero[firstVertex + 3] = new Vector2(card.uvRect.xMax, card.uvRect.yMax);

            uvTwo[firstVertex] = new Vector2(0f, 0f);
            uvTwo[firstVertex + 1] = new Vector2(1f, 0f);
            uvTwo[firstVertex + 2] = new Vector2(0f, 1f);
            uvTwo[firstVertex + 3] = new Vector2(1f, 1f);

            Color cardColor = new Color(card.tint01, 1f, 1f, 1f);
            for (int cornerIndex = 0; cornerIndex < VerticesPerCard; cornerIndex++)
            {
                normals[firstVertex + cornerIndex] = faceNormal;
                colors[firstVertex + cornerIndex] = cardColor;
            }

            // Clockwise seen from the camera is the front face in Unity.
            int firstIndex = cardIndex * IndicesPerCard;
            indices[firstIndex] = firstVertex;
            indices[firstIndex + 1] = firstVertex + 2;
            indices[firstIndex + 2] = firstVertex + 3;
            indices[firstIndex + 3] = firstVertex;
            indices[firstIndex + 4] = firstVertex + 3;
            indices[firstIndex + 5] = firstVertex + 1;
        }

        mesh.indexFormat = vertexCount > SixteenBitVertexLimit ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.uv = uvZero;
        mesh.SetUVs(2, uvTwo);
        mesh.triangles = indices;
        mesh.RecalculateBounds();
        return mesh;
    }
}
#endif
