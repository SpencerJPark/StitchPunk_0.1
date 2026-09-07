// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Baked actor profile: every named animation across every layer, resolved by key and facing at runtime.</summary>
    public struct ActorProfileBlob
    {
        public int schemaVersion; // bumped on any layout change, stamped at bake

        public AnimationDirections turnDirections;

        public byte layerCount;

        public BlobArray<ActorAnimationBlob> animations; // sorted ascending by animationKey; binary-search key array
    }

    public struct ActorAnimationBlob
    {
        public uint animationKey;

        public byte layerIndex;

        public bool hasDirections; // false = clip is used outright; true = slots is resolved against facing

        public ClipId clip;

        public DirectionSlotsBlob slots;

        public LoopMode loop;

        public float speed;

        public float blendIn; // seconds; NaN = the clip's own default

        public RagdollTrigger ragdollTrigger;

        public uint ragdollAtEventKey; // 0 = at play, immediately
    }

    public struct DirectionSlotsBlob
    {
        public ClipId southEast;
        public ClipId northEast;
        public ClipId south;
        public ClipId north;
        public ClipId east;

        public AnimationDirections effectiveDirections;

        public ClipId GetSlot(Direction eastSideFacing)
        {
            switch (eastSideFacing)
            {
                case Direction.SouthEast: return southEast;
                case Direction.NorthEast: return northEast;
                case Direction.South: return south;
                case Direction.North: return north;
                case Direction.East: return east;
                default: return default;
            }
        }

        // The per-entry fold: an entry with fewer authored directions than the actor turns through
        // folds onto whatever it actually has, so a Two-coverage entry on a Six-turning actor
        // mirrors left/right instead of returning an empty ClipId for every rear facing.
        public ClipId ResolveSlot(Direction eastSideFacing)
        {
            FacingResolver.ResolveClipFacing(
                eastSideFacing, effectiveDirections, out Direction foldedFacing, out bool _);
            return GetSlot(foldedFacing);
        }
    }
}
