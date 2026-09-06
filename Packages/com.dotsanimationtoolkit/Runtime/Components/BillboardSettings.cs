// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Everything <see cref="BillboardMath"/> needs to resolve one billboard root's orientation:
    /// the authored configuration plus the two channels a clip can key. A default-constructed
    /// value is inert — disabled, with zero blend weight.
    /// </summary>
    public struct BillboardSettings
    {
        public BillboardMode mode;

        public float3 constraintAxis; // world space, normalized at bake; ignored by every mode but AxisConstrained

        public float frozenYaw; // radians, held by BillboardMode.FrozenYaw

        public float angleOffsetRadians; // radians about the resolved frame's up axis, after facing resolves and before snapping

        public float blendWeight; // [0, 1] against the animated pose; 1 = fully billboarded, 0 = untouched

        // MarshalAs is load-bearing: a bare C# bool has no fixed width, so this struct would not
        // be blittable, and it crosses a [BurstCompile] entry point where only blittable types
        // are allowed (BC1063).
        [MarshalAs(UnmanagedType.U1)] public bool enabled;

        public int snapSteps; // discrete facings per full turn; below 2 = no snapping

        public float snapPhaseRadians; // phase of the snap wheel, so steps can straddle cardinal directions

        public float clampHalfArcRadians; // half-width of the turn arc from rest orientation; negative = no clamping

        public bool SnapEnabled
        {
            get { return snapSteps >= 2; }
        }

        public bool ClampEnabled
        {
            get { return clampHalfArcRadians >= 0f; }
        }
    }
}
