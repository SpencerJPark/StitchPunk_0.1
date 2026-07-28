// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// One emitted animation event on the actor root (architecture sections 5.2, 5.5).
    /// <c>EventEmissionSystem</c> clears the buffer every frame before emitting; events are valid
    /// from that system's execution until its next execution (documented 1-frame latency contract
    /// for consumers ordered earlier in the frame).
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct AnimEventOutput : IBufferElementData
    {
        /// <summary>
        /// Typed event key: user keys ≥ 16 (rule V09); 0 invalid; 1–15 reserved
        /// (<see cref="ReservedEventKeys.ClipFinished"/> = 1,
        /// <see cref="ReservedEventKeys.ClipResolveFailed"/> = 2).
        /// </summary>
        public uint eventKey;

        /// <summary>The playback layer that emitted the event.</summary>
        public byte layerIndex;

        /// <summary>Id of the clip the event belongs to.</summary>
        public ClipId clip;

        /// <summary>User integer payload copied from the authored marker.</summary>
        public int intParam;

        /// <summary>User float payload copied from the authored marker.</summary>
        public float floatParam;
    }

    /// <summary>
    /// Enableable gate on the actor root: enabled while the <see cref="AnimEventOutput"/> buffer
    /// holds events this frame, so event-less actors cost consumers nothing
    /// (architecture sections 5.2, 5.5). Baked disabled.
    /// </summary>
    public struct AnimEventsPending : IComponentData, IEnableableComponent
    {
    }
}
