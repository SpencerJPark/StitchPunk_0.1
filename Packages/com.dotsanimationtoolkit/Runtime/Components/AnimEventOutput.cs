// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// One emitted animation event on the actor root. <c>EventEmissionSystem</c> clears the buffer
    /// every frame before emitting; events are valid only until that system's next execution.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct AnimEventOutput : IBufferElementData
    {
        public uint eventKey; // user keys >= 16; 0 invalid; 1-15 reserved, see ReservedEventKeys

        public byte layerIndex;

        public ClipId clip;

        public int intParam;

        public float floatParam;

        public uint animationKey; // the emitting layer's PlaybackLayer.animationKey; 0 for a raw clip
    }

    /// <summary>
    /// Enableable gate on the actor root: enabled while <see cref="AnimEventOutput"/> holds events
    /// this frame, so event-less actors cost consumers nothing. Baked disabled.
    /// </summary>
    public struct AnimEventsPending : IComponentData, IEnableableComponent
    {
    }
}
