#ifndef STITCHPUNK_TOOLKIT_BILLBOARD_INCLUDED
#define STITCHPUNK_TOOLKIT_BILLBOARD_INCLUDED

// =====================================================================================
// DOTS Animation Toolkit — billboarding (architecture section 6.1, 6.3; amendment A39)
//
// Turns a quad to face the viewer in the vertex stage.
//
// STANDALONE BY DESIGN. This file includes nothing, declares no properties, and reads no
// globals of its own — every input arrives as a parameter. That is deliberate so it can be
// dropped into any URP project, called from a Custom Function node, a hand-written pass, or
// another package's shader, without dragging the rest of this toolkit along.
//
// ------------------------------------------------------------------------------------
// THE ONE RULE THAT MATTERS: never derive facing from UNITY_MATRIX_V.
//
// A billboard must present the SAME geometry in every pass. During shadow rendering the
// view matrix belongs to the LIGHT, so a view-matrix billboard silently turns quads to
// face the light and casts the shadow of a shape the camera never sees. The facing inputs
// here are therefore passed in by the caller from values that are constant across all
// passes of a frame: the camera position (`_WorldSpaceCameraPos`) and, for screen-aligned
// modes, a camera forward vector the host supplies once per frame.
//
// Amendment A39 added the forward vector. The rule was never "use the camera position" —
// it is "use something pass-invariant", and a host-written forward is exactly as
// pass-invariant as the position.
// =====================================================================================

// Billboard modes, matching BillboardParamsProperty.Value.x on the CPU side.
#define TOOLKIT_BILLBOARD_OFF           0.0
#define TOOLKIT_BILLBOARD_FULL          1.0
#define TOOLKIT_BILLBOARD_UPRIGHT       2.0
#define TOOLKIT_BILLBOARD_FROZEN_YAW    3.0
#define TOOLKIT_BILLBOARD_SCREEN_ALIGNED 4.0

// Below this, a direction vector is treated as degenerate and the quad is left unrotated.
// Rotating by a near-zero vector is how billboards produce single-frame flips when the
// camera passes directly through a pivot.
#define TOOLKIT_BILLBOARD_EPSILON 1e-6

// -------------------------------------------------------------------------------------
// Builds an orthonormal basis that maps +Z onto `forward`, with `up` as the reference.
// Returns a rotation matrix in world space.
// -------------------------------------------------------------------------------------
float3x3 ToolkitBillboardBasis(float3 forward, float3 up)
{
    float3 zAxis = normalize(forward);
    float3 xAxis = cross(up, zAxis);

    // forward parallel to up: pick any perpendicular rather than emitting NaNs. This is the
    // camera looking straight down at an upright billboard, which is rare and must not flash.
    float xAxisLength = length(xAxis);
    xAxis = xAxisLength < TOOLKIT_BILLBOARD_EPSILON
        ? float3(1.0, 0.0, 0.0)
        : xAxis / xAxisLength;

    float3 yAxis = cross(zAxis, xAxis);
    return float3x3(xAxis, yAxis, zAxis);
}

// -------------------------------------------------------------------------------------
// The facing direction a mode wants, in world space, pointing FROM the pivot TOWARD the
// viewer.
//
//   cameraPositionWS — the rendering camera's world position (pass-invariant).
//   cameraForwardWS  — the rendering camera's world forward (pass-invariant, host-written).
//   pivotWS          — the billboard's pivot in world space.
// -------------------------------------------------------------------------------------
float3 ToolkitBillboardFacing(float mode, float3 pivotWS, float3 cameraPositionWS, float3 cameraForwardWS)
{
    // Screen-aligned: every quad takes the SAME orientation, so the facing direction is the
    // camera's forward negated — pointing back at the viewer. This is the classic 2.5D look
    // and it is what the Stitch Punk host shipped before migrating (A39); it is a different
    // look from spherical, not a worse one, and quads far from screen centre show it most.
    if (mode == TOOLKIT_BILLBOARD_SCREEN_ALIGNED)
    {
        float forwardLength = length(cameraForwardWS);

        // A host that never writes the forward gets spherical rather than a degenerate quad.
        // Degrading to a different-but-correct look beats degrading to a collapsed one.
        return forwardLength < TOOLKIT_BILLBOARD_EPSILON
            ? cameraPositionWS - pivotWS
            : -cameraForwardWS;
    }

    // Every other mode is spherical: each quad turns to face the camera POINT.
    return cameraPositionWS - pivotWS;
}

// -------------------------------------------------------------------------------------
// Rotates `positionOS` about `pivotOS` so the quad faces the viewer.
//
// Returns the new object-space position. Callers apply it before the usual object-to-world
// transform, so the result flows through every pass identically.
//
//   positionOS       — the vertex, in object space.
//   pivotOS          — the point to rotate about, in object space (usually zero).
//   billboardParams  — x = mode, y = frozen yaw in radians, zw reserved. Matches
//                      BillboardParamsProperty on the CPU.
//   cameraPositionWS — `_WorldSpaceCameraPos`.
//   cameraForwardWS  — host-written camera forward; may be zero if unused.
//   objectToWorld    — UNITY_MATRIX_M.
//   worldToObject    — UNITY_MATRIX_I_M.
// -------------------------------------------------------------------------------------
float3 BillboardTransform(
    float3 positionOS,
    float3 pivotOS,
    float4 billboardParams,
    float3 cameraPositionWS,
    float3 cameraForwardWS,
    float4x4 objectToWorld,
    float4x4 worldToObject)
{
    float mode = billboardParams.x;
    if (mode == TOOLKIT_BILLBOARD_OFF)
    {
        return positionOS;
    }

    float3 pivotWS = mul(objectToWorld, float4(pivotOS, 1.0)).xyz;
    float3 facingWS = ToolkitBillboardFacing(mode, pivotWS, cameraPositionWS, cameraForwardWS);

    if (length(facingWS) < TOOLKIT_BILLBOARD_EPSILON)
    {
        return positionOS;
    }

    // Upright: the quad spins about world Y only, so a tree or a standing character never
    // leans. Flattening the facing vector is what restricts the rotation to one axis.
    if (mode == TOOLKIT_BILLBOARD_UPRIGHT)
    {
        facingWS.y = 0.0;
        if (length(facingWS) < TOOLKIT_BILLBOARD_EPSILON)
        {
            return positionOS;
        }
    }

    float3x3 rotationWS = ToolkitBillboardBasis(facingWS, float3(0.0, 1.0, 0.0));

    // Frozen yaw: pitch keeps tracking the camera while yaw is held at an authored angle.
    // This is the corpse case — a dead body should not pirouette to follow the viewer, but
    // it should still be seen rather than edge-on. The CPU freezes the yaw at the moment of
    // death and writes it into billboardParams.y.
    if (mode == TOOLKIT_BILLBOARD_FROZEN_YAW)
    {
        float frozenYaw = billboardParams.y;
        float sinYaw;
        float cosYaw;
        sincos(frozenYaw, sinYaw, cosYaw);

        float3x3 frozenYawRotation = float3x3(
            cosYaw, 0.0, sinYaw,
            0.0, 1.0, 0.0,
            -sinYaw, 0.0, cosYaw);

        // Strip the camera-derived yaw, keep its pitch, then substitute the frozen yaw.
        float3 facingFlat = float3(facingWS.x, 0.0, facingWS.z);
        if (length(facingFlat) >= TOOLKIT_BILLBOARD_EPSILON)
        {
            float3x3 cameraYaw = ToolkitBillboardBasis(facingFlat, float3(0.0, 1.0, 0.0));
            float3x3 pitchOnly = mul(transpose(cameraYaw), rotationWS);
            rotationWS = mul(frozenYawRotation, pitchOnly);
        }
        else
        {
            rotationWS = frozenYawRotation;
        }
    }

    // Rotate about the pivot in world space, then return to object space so the caller's
    // pipeline is unchanged. Doing the rotation in world space is what makes the result
    // independent of however the object itself happens to be oriented.
    float3 positionWS = mul(objectToWorld, float4(positionOS, 1.0)).xyz;
    float3 offsetWS = positionWS - pivotWS;
    float3 rotatedWS = pivotWS + mul(rotationWS, offsetWS);
    return mul(worldToObject, float4(rotatedWS, 1.0)).xyz;
}

// -------------------------------------------------------------------------------------
// Convenience overload for callers that do not use screen-aligned mode and have no camera
// forward to pass. Kept separate rather than defaulted: a silent default is how half the
// callers would stop honouring a mode they had opted into (the ClipSampler.CompositeLayers
// precedent, amendment A34).
// -------------------------------------------------------------------------------------
float3 BillboardTransformSpherical(
    float3 positionOS,
    float3 pivotOS,
    float4 billboardParams,
    float3 cameraPositionWS,
    float4x4 objectToWorld,
    float4x4 worldToObject)
{
    return BillboardTransform(
        positionOS, pivotOS, billboardParams, cameraPositionWS,
        float3(0.0, 0.0, 0.0), objectToWorld, worldToObject);
}

#endif // STITCHPUNK_TOOLKIT_BILLBOARD_INCLUDED
