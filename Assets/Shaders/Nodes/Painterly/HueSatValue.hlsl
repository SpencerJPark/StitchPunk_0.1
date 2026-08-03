// HueSatValue — the "recolor anything" control from the painterly setup.
// Shifts hue around the color wheel and scales saturation/value, so one ramp
// (e.g. the green grass ramp) can become any other palette without editing
// the ramp colors themselves.
//
// Signature is void + out (not a return value) to match every other node in this
// library — reflected out-params get predictable slot ids after the inputs, which
// is what graph edits depend on. Slots: 1 inputColor, 2 hueShift, 3 saturation,
// 4 value, 5 outputColor.

#include "ShaderApiReflectionSupport.hlsl"
#include "PainterlyCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.HueSatValue</sg:ProviderKey>
///     <sg:DisplayName>Hue Sat Value</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "inputColor">
///     <sg:DisplayName>Color</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>0.22, 0.35, 0.12</sg:Default>
///</paramhints>
///<paramhints name = "hueShift">
///     <sg:DisplayName>Hue Shift</sg:DisplayName>
///     <sg:Range>-0.5, 0.5</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "saturation">
///     <sg:DisplayName>Saturation</sg:DisplayName>
///     <sg:Range>0, 2</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
///<paramhints name = "value">
///     <sg:DisplayName>Value</sg:DisplayName>
///     <sg:Range>0, 2</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void HueSatValue(
    float3 inputColor,
    float hueShift,
    float saturation,
    float value,
    out float3 outputColor)
{
    outputColor = PainterlyApplyHueSatValue(inputColor, hueShift, saturation, value);
}
