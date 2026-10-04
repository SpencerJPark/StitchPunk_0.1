// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    // NegativeZ = toward a default camera, like Unity's built-in Quad.
    public enum CutoutFacing
    {
        NegativeZ,
        PositiveZ
    }

    // Blender's object mode, and edit mode's vertex / edge select modes.
    public enum CutoutCanvasMode
    {
        Object,
        EditVertices,
        EditEdges
    }

    public enum CutoutNormalMode
    {
        Flat,
        Rounded
    }
}
