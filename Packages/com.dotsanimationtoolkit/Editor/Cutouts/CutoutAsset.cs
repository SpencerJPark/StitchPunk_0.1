// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    [CreateAssetMenu(fileName = "NewCutout", menuName = "DOTS Animation Toolkit/Cutout", order = 41)]
    public sealed class CutoutAsset : ScriptableObject
    {
        public const int MinimumVertexCount = 3;

        [Tooltip("The art the cutout is drawn over: a FlipbookAsset or a bare Texture2DArray.")]
        public UnityEngine.Object flipbook;

        [Tooltip("Pixels per world unit, with the same meaning as a sprite's.")]
        public float pixelsPerUnit = 100f;

        [Tooltip("Outline in frame pixel space, (0,0) bottom-left. Empty means not drawn yet.")]
        public List<Vector2> outlinePixels = new List<Vector2>();

        [Tooltip("Pivot of the mesh in frame pixels.")]
        public Vector2 originPixels;

        [Tooltip("Where the origin sits on the canvas grid, in world units. Editor-only, never baked.")]
        public Vector2 artPositionWorld;

        [Tooltip("Drawn edges: pairs of outline vertex indices (x < y) the triangulation must keep.")]
        public List<Vector2Int> innerEdges = new List<Vector2Int>();

        [Tooltip("Which way the front face points. NegativeZ faces a default camera, like Unity's built-in Quad.")]
        public CutoutFacing facing = CutoutFacing.NegativeZ;

        [Tooltip("Flat uses one normal for the whole mesh; Rounded bends normals toward the edges.")]
        public CutoutNormalMode normalMode = CutoutNormalMode.Flat;

        [Tooltip("How strongly Rounded normals bend toward the edges.")]
        [Range(0f, 1f)] public float roundness = 0.5f;

        [Tooltip("Vertex count the Fit action aims for.")]
        public int fitVertexBudget = 8;

        [Tooltip("Pixels the Fit outline sits outside the opaque art.")]
        public float fitPaddingPixels = 2f;

        [Tooltip("Sizing aid drawn behind the outline. Never baked into the mesh.")]
        public Texture2D referenceImage;

        [Tooltip("Where the reference image sits in world units.")]
        public Rect referenceRectWorld = new Rect(-0.5f, 0f, 1f, 1f);

        [Tooltip("Opacity of the reference image overlay.")]
        [Range(0f, 1f)] public float referenceOpacity = 0.6f;

        [Tooltip("The generated mesh asset.")]
        public Mesh outputMesh;

        [Tooltip("Project-relative .asset path of the generated mesh.")]
        public string outputPath = string.Empty;

        public Vector2Int FrameSize
        {
            get
            {
                Texture2DArray array = ResolveArray();
                return array == null ? Vector2Int.zero : new Vector2Int(array.width, array.height);
            }
        }

        // The array the cutout is drawn over: the flipbook's texture, or the bare array itself; null when unset.
        public Texture2DArray ResolveArray()
        {
            FlipbookAsset flipbookAsset = flipbook as FlipbookAsset;
            if (flipbookAsset != null)
            {
                return flipbookAsset.texture;
            }
            return flipbook as Texture2DArray;
        }

        // Replaces the outline with the full frame quad and puts the origin at bottom-centre; no-op with no array.
        public void ResetToFullQuad()
        {
            Vector2Int frameSize = FrameSize;
            if (frameSize.x <= 0 || frameSize.y <= 0)
            {
                return;
            }

            float width = frameSize.x;
            float height = frameSize.y;
            outlinePixels = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(width, 0f),
                new Vector2(width, height),
                new Vector2(0f, height)
            };
            originPixels = new Vector2(width * 0.5f, 0f);
            // The outline was replaced, so the old vertex indices mean nothing.
            innerEdges.Clear();
        }
    }
}
