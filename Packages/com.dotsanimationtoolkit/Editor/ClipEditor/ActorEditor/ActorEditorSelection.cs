// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Which kind of row is selected in the Actor Editor's layers tree, and which one.</summary>
    public enum ActorEditorSelectionKind : byte { None = 0, Profile = 1, Layer = 2, Animation = 3 }

    /// <summary>One selected row: the profile itself, a layer, or one animation on a layer.</summary>
    public struct ActorEditorSelection
    {
        public ActorEditorSelectionKind kind;
        public int layerIndex;      // Layer/Animation
        public int animationIndex;  // Animation only
        public static ActorEditorSelection None => default;
    }
}
