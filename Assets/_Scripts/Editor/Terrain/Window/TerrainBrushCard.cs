#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

public static class TerrainBrushCard
{
    public static VisualElement Make(out Action refresh)
    {
        VisualElement body;
        VisualElement card = TerrainUi.MakeCard("Brush", out body);

        Action refreshSize;
        Action refreshStrength;
        Action refreshHardness;

        body.Add(TerrainUi.MakeSliderRow(
            "Size", BrushState.MinimumRadiusMetres, BrushState.MaximumRadiusMetres,
            () => BrushState.radiusMetres, (float value) => BrushState.radiusMetres = value,
            "How wide the brush is, in metres.", out refreshSize));
        body.Add(TerrainUi.MakeSliderRow(
            "Strength", 0f, 1f,
            () => BrushState.strength, (float value) => BrushState.strength = value,
            "How much each stroke changes the ground.", out refreshStrength));
        body.Add(TerrainUi.MakeSliderRow(
            "Hardness", 0f, 1f,
            () => BrushState.hardness, (float value) => BrushState.hardness = value,
            "0 is a soft edge, 1 a hard edge.", out refreshHardness));

        refresh = () =>
        {
            refreshSize();
            refreshStrength();
            refreshHardness();
        };
        return card;
    }
}
#endif
