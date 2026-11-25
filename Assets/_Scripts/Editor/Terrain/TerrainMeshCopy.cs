#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Copies mesh geometry through the Mesh API so the GPU buffers are re-uploaded;
// EditorUtility.CopySerialized rewrites CPU data only and the renderer keeps drawing the old mesh.
public static class TerrainMeshCopy
{
    const int UvChannelCount = 8;

    public static void CopyGeometryInto(Mesh source, Mesh destination)
    {
        string preservedName = destination.name;
        destination.Clear();
        destination.name = preservedName;
        destination.indexFormat = source.indexFormat;

        List<Vector3> vertices = new List<Vector3>();
        source.GetVertices(vertices);
        destination.SetVertices(vertices);

        if (source.HasVertexAttribute(VertexAttribute.Normal))
        {
            List<Vector3> normals = new List<Vector3>();
            source.GetNormals(normals);
            destination.SetNormals(normals);
        }
        if (source.HasVertexAttribute(VertexAttribute.Tangent))
        {
            List<Vector4> tangents = new List<Vector4>();
            source.GetTangents(tangents);
            destination.SetTangents(tangents);
        }
        if (source.HasVertexAttribute(VertexAttribute.Color))
        {
            List<Color> colors = new List<Color>();
            source.GetColors(colors);
            destination.SetColors(colors);
        }

        for (int channel = 0; channel < UvChannelCount; channel++)
        {
            VertexAttribute attribute = VertexAttribute.TexCoord0 + channel;
            if (!source.HasVertexAttribute(attribute)) continue;
            int dimension = source.GetVertexAttributeDimension(attribute);
            if (dimension == 2)
            {
                List<Vector2> uvs = new List<Vector2>();
                source.GetUVs(channel, uvs);
                destination.SetUVs(channel, uvs);
            }
            else if (dimension == 3)
            {
                List<Vector3> uvs = new List<Vector3>();
                source.GetUVs(channel, uvs);
                destination.SetUVs(channel, uvs);
            }
            else
            {
                List<Vector4> uvs = new List<Vector4>();
                source.GetUVs(channel, uvs);
                destination.SetUVs(channel, uvs);
            }
        }

        destination.subMeshCount = source.subMeshCount;
        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            destination.SetTriangles(source.GetTriangles(subMesh), subMesh, false);
        }

        destination.RecalculateBounds();
        destination.UploadMeshData(false);
    }
}
#endif
