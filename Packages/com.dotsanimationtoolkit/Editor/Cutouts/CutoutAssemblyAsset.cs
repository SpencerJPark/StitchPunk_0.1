// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    [Serializable]
    public sealed class CutoutAssemblyPiece
    {
        public string name;

        public CutoutAsset cutout;

        [Tooltip("Where the piece's origin sits in the assembly, in world units.")]
        public Vector2 positionWorld;

        [Tooltip("Rotation added on top of the cutout's own, in degrees.")]
        public float extraRotationDegrees;

        [Tooltip("Higher draws in front.")]
        public int drawOrder;

        [Tooltip("Index of the parent piece in the list; -1 parents the piece to the root.")]
        public int parentPieceIndex = -1;
    }

    [CreateAssetMenu(fileName = "NewCutoutAssembly", menuName = "DOTS Animation Toolkit/Cutout Assembly", order = 43)]
    public sealed class CutoutAssemblyAsset : ScriptableObject
    {
        public List<CutoutAssemblyPiece> pieces = new List<CutoutAssemblyPiece>();

        [Tooltip("LayeredSet keeps one mesh per piece; Merged bakes them into a single mesh.")]
        public CutoutAssemblyOutputKind outputKind = CutoutAssemblyOutputKind.LayeredSet;

        [Tooltip("Material the assembled pieces are drawn with.")]
        public Material material;

        [Tooltip("Project folder the assembly's output assets are written to.")]
        public string outputFolder = string.Empty;

        [Tooltip("The prefab produced by the last assemble.")]
        public GameObject outputPrefab;

        [Tooltip("The rig produced by the last assemble.")]
        public RigAsset outputRig;

        [Tooltip("The merged mesh produced by the last assemble (Merged only).")]
        public Mesh outputMergedMesh;
    }
}
