// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>Actor-root component naming which way the actor faces. The toolkit never derives <see cref="facing"/>; only re-picks clips against it.</summary>
    public struct ActorFacing : IComponentData
    {
        public Direction facing; // host-written; what "forward" means is the host's call

        public Direction appliedFacing; // the toolkit's last re-pick; compare to facing to detect a pending change
    }
}
