// ToolkitBillboardVertex — turns a quad to face the viewer, in the VERTEX stage.
//
// Wire it between Position (Object Space) and the master stack's Vertex Position block.
// Everything downstream — the fragment stage, every generated pass — is untouched, which is
// the whole point: a billboard that displaces in the colour pass but not in ShadowCaster
// casts the shadow of a shape the camera never sees.
//
// VERTEX STAGE ONLY. Wiring this into a fragment chain compiles and does nothing useful.
//
// The maths lives in the toolkit package and is not duplicated here. This file exists so the
// package's standalone include can be dropped onto a graph as an ordinary node, which is the
// split architecture section 6.1 always intended: the package ships portable HLSL that any
// Unity 6.5 project can use, and this host wraps it in its own reflection-node convention.

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitBillboard.hlsl"

// Written once per frame by ToolkitCameraBinder. Read here rather than exposed as a port on
// purpose: screen-aligned mode silently degrades to spherical when the forward is zero, and a
// port someone forgot to wire would look exactly like a working billboard that curves at the
// screen edges. That failure cost real time to diagnose once already.
float4 _ToolkitCameraForward;

///<funchints>
///     <sg:ProviderKey>StitchPunk.ToolkitBillboardVertex</sg:ProviderKey>
///     <sg:DisplayName>Billboard Vertex</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Animation</sg:SearchCategory>
///</funchints>
///<paramhints name = "positionOS">
///     <sg:DisplayName>Position (Object)</sg:DisplayName>
///</paramhints>
///<paramhints name = "pivotOS">
///     <sg:DisplayName>Pivot (Object)</sg:DisplayName>
///     <sg:Default>0, 0, 0</sg:Default>
///</paramhints>
///<paramhints name = "billboardMode">
///     <sg:DisplayName>Mode</sg:DisplayName>
///     <sg:Range>0, 5</sg:Range>
///     <sg:Default>4</sg:Default>
///</paramhints>
///<paramhints name = "frozenYawRadians">
///     <sg:DisplayName>Frozen Yaw</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ToolkitBillboardVertex(
    float3 positionOS,
    float3 pivotOS,
    float billboardMode,
    float frozenYawRadians,
    out float3 displacedPositionOS)
{
#if defined(SHADERGRAPH_PREVIEW)
    // The preview has no meaningful camera or object matrices, and a billboard resolved
    // against fabricated ones renders as a collapsed sliver that reads as a broken node.
    displacedPositionOS = positionOS;
#else
    displacedPositionOS = BillboardTransform(
        positionOS,
        pivotOS,
        float4(billboardMode, frozenYawRadians, 0.0, 0.0),
        _WorldSpaceCameraPos,
        _ToolkitCameraForward.xyz,
        UNITY_MATRIX_M,
        UNITY_MATRIX_I_M);
#endif
}
