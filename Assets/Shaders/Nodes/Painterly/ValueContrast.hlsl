// ValueContrast — add or remove contrast in the grayscale mask value before it
// hits the color ramp (the "add contrast / subtract contrast" control from the
// painterly setup). Pivot is the gray point the contrast rotates around;
// brightness shifts the whole value up/down.

#include "ShaderApiReflectionSupport.hlsl"
#include "PainterlyCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ValueContrast</sg:ProviderKey>
///     <sg:DisplayName>Value Contrast</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "value">
///     <sg:DisplayName>Value</sg:DisplayName>
///</paramhints>
///<paramhints name = "contrast">
///     <sg:DisplayName>Contrast</sg:DisplayName>
///     <sg:Range>0, 4</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
///<paramhints name = "pivot">
///     <sg:DisplayName>Pivot</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.5</sg:Default>
///</paramhints>
///<paramhints name = "brightness">
///     <sg:DisplayName>Brightness</sg:DisplayName>
///     <sg:Range>-1, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float ValueContrast(float value, float contrast, float pivot, float brightness)
{
    return PainterlyApplyValueContrast(value, contrast, pivot, brightness);
}
