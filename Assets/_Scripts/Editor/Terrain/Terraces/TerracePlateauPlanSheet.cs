#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

// Natural Cliffs N2: renders every plateau roll over the 3x3 test tiles to one PNG plus a metrics JSON. Pure CPU, no scene or tile asset touched.
public static class TerracePlateauPlanSheet
{
    private const int RollCount = 6;
    // Tiles -2..2 are rasterized so the outer measured ring has real neighbours instead of clamped edge padding.
    private const int ContextTileRadius = 2;
    private const int TilesPerSide = 3;
    private const int CellsPerSide = 100;
    private const int PixelsPerMetre = 2;
    private const int TilePixels = CellsPerSide * PixelsPerMetre;
    private const int BlockPixels = TilePixels * TilesPerSide;
    private const int GutterPixels = 6;
    private const int HeaderPixels = 14;
    private const int SignedDistanceSide = CellsPerSide * PixelsPerMetre + 1;
    private const float OutlineHalfWidthMetres = 0.35f;
    private const float BothKindsMinimumMetres = 0.5f;

    private static readonly Color32 LevelZeroColour = new Color32(0x6f, 0x8a, 0x4f, 255);
    private static readonly Color32 LevelOneLowColour = new Color32(0xa7, 0xb8, 0x6a, 255);
    private static readonly Color32 LevelOneTallColour = new Color32(0xd9, 0xc2, 0x7a, 255);
    private static readonly Color32 LevelTwoLowColour = new Color32(0xc9, 0xd6, 0x8f, 255);
    private static readonly Color32 LevelTwoTallColour = new Color32(0xf2, 0xe2, 0xa0, 255);
    private static readonly Color32 OutlineColour = new Color32(0x2b, 0x2b, 0x2b, 255);
    private static readonly Color32 BackgroundColour = new Color32(0x18, 0x18, 0x18, 255);
    private static readonly Color32 TextColour = new Color32(255, 255, 255, 255);

    private const string GlyphCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // 3 wide x 5 tall, row-major, top row in the highest bits.
    private static readonly ushort[] GlyphBits =
    {
        0b111_101_101_101_111, 0b010_110_010_010_111, 0b111_001_111_100_111, 0b111_001_111_001_111, 0b101_101_111_001_001,
        0b111_100_111_001_111, 0b111_100_111_101_111, 0b111_001_001_001_001, 0b111_101_111_101_111, 0b111_101_111_001_111,
        0b010_101_111_101_101, 0b110_101_110_101_110, 0b011_100_100_100_011, 0b110_101_101_101_110, 0b111_100_110_100_111,
        0b111_100_110_100_100, 0b011_100_101_101_011, 0b101_101_111_101_101, 0b111_010_010_010_111, 0b001_001_001_101_010,
        0b101_101_110_101_101, 0b100_100_100_100_111, 0b101_111_111_101_101, 0b110_101_101_101_101, 0b010_101_101_101_010,
        0b110_101_110_100_100, 0b010_101_101_111_011, 0b110_101_110_101_101, 0b011_100_010_001_110, 0b111_010_010_010_010,
        0b101_101_101_101_111, 0b101_101_101_101_010, 0b101_101_111_111_101, 0b101_101_010_101_101, 0b101_101_010_010_010,
        0b111_001_010_100_111,
    };

    private class RollAggregate
    {
        public float totalOutline;
        public float axisAlignedOutline;
        public float lowOutline;
        public float longestStraight;
        public float worstStraightExcessOverTileLimit = float.MinValue;
        public float minCompactness = float.MaxValue;
        public float maxCompactness = float.MinValue;
        public int compactnessCount;
        public int neckViolationCells;
        public float minTerracedShare = float.MaxValue;
        public float maxTerracedShare = float.MinValue;
    }

    [MenuItem("Stitch Punk/Terrain/Debug/Render Plateau Plan Sheet")]
    public static void RenderFromMenu()
    {
        TerrainGenerationProfile profile = AssetDatabase.LoadAssetAtPath<TerrainGenerationProfile>("Assets/Terrain/RandomWorld/RandomWorldGeneration.asset");
        int worldSeed = profile != null ? profile.seed : 1;
        string outputFolder = "Assets/_Vault/Tasks/Captures/NaturalCliffs_" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string summary = RenderPlanSheet(worldSeed, outputFolder, TerracePlateauStyleDefaults.CreateDefaultStyles(), 1.5f, 2, 150f);
        Debug.Log(summary);
    }

    public static string RenderPlanSheet(int worldSeed, string outputFolder, IReadOnlyList<TerracePlateauStyle> styles,
        float cornerRadiusMetres, int maxLevel, float minPlateauAreaSquareMetres)
    {
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int sheetWidth = RollCount * BlockPixels + (RollCount - 1) * GutterPixels;
        int sheetHeight = HeaderPixels + BlockPixels;
        Color32[] sheet = new Color32[sheetWidth * sheetHeight];
        for (int pixelIndex = 0; pixelIndex < sheet.Length; pixelIndex++)
        {
            sheet[pixelIndex] = BackgroundColour;
        }

        StringBuilder tileJson = new StringBuilder();
        RollAggregate[] rollAggregates = new RollAggregate[RollCount];
        bool[] rollHasSparseAndDenseTile = new bool[RollCount];
        bool[] rollHasBothKinds = new bool[RollCount];
        bool firstTileEntry = true;
        int[] levels = new int[CellsPerSide * CellsPerSide];
        byte[] kinds = new byte[CellsPerSide * CellsPerSide];
        List<TerrainTileData> createdData = new List<TerrainTileData>();

        try
        {
            for (int rollIndex = 0; rollIndex < RollCount; rollIndex++)
            {
                Dictionary<Vector2Int, TerrainTileData> rollTiles = new Dictionary<Vector2Int, TerrainTileData>();
                Func<Vector2Int, TerrainTileData> lookup = coordinate =>
                {
                    TerrainTileData found;
                    return rollTiles.TryGetValue(coordinate, out found) ? found : null;
                };

                for (int tileZ = -ContextTileRadius; tileZ <= ContextTileRadius; tileZ++)
                {
                    for (int tileX = -ContextTileRadius; tileX <= ContextTileRadius; tileX++)
                    {
                        Vector2Int tile = new Vector2Int(tileX, tileZ);
                        TerracePlateauRaster.RasterizeTile(worldSeed, tile, rollIndex, styles, maxLevel, minPlateauAreaSquareMetres, levels, kinds);
                        TerrainTileData data = ScriptableObject.CreateInstance<TerrainTileData>();
                        createdData.Add(data);
                        data.coordinate = tile;
                        data.baseTerraceLevels = new byte[levels.Length];
                        for (int cellIndex = 0; cellIndex < levels.Length; cellIndex++)
                        {
                            data.baseTerraceLevels[cellIndex] = (byte)(levels[cellIndex] + TerrainTileData.TerraceLevelBias);
                        }
                        data.baseTerraceStepKinds = (byte[])kinds.Clone();
                        rollTiles[tile] = data;
                    }
                }

                RollAggregate aggregate = new RollAggregate();
                rollAggregates[rollIndex] = aggregate;
                int sparseTileCount = 0;
                int denseTileCount = 0;

                for (int tileZ = -1; tileZ <= 1; tileZ++)
                {
                    for (int tileX = -1; tileX <= 1; tileX++)
                    {
                        Vector2Int tile = new Vector2Int(tileX, tileZ);
                        TerrainTileData data = rollTiles[tile];
                        int styleIndex = TerracePlateauField.StyleIndexFor(worldSeed, new int2(tileX, tileZ), rollIndex, styles);
                        string styleName = styles[styleIndex].styleName;

                        int tileOriginX = rollIndex * (BlockPixels + GutterPixels) + (tileX + 1) * TilePixels;
                        int tileOriginTop = HeaderPixels + (1 - tileZ) * TilePixels;
                        PaintTile(sheet, sheetWidth, sheetHeight, tileOriginX, tileOriginTop, data, lookup, maxLevel, cornerRadiusMetres);
                        DrawText(sheet, sheetWidth, sheetHeight, tileOriginX + 3, tileOriginTop + 3, styleName, 2);

                        TerracePlateauTileMetrics metrics = TerracePlateauMetrics.Measure(data, lookup, cornerRadiusMetres, maxLevel);
                        AccumulateMetrics(aggregate, metrics);
                        // Cliff-line styles (Escarpment) are allowed longer straight stretches.
                        float straightLimitMetres = styles[styleIndex].grainStretch > 1.5f ? 20f : 14f;
                        aggregate.worstStraightExcessOverTileLimit = Mathf.Max(aggregate.worstStraightExcessOverTileLimit,
                            metrics.longestStraightStretchMetres - straightLimitMetres);
                        if (metrics.terracedAreaShare < 0.10f)
                        {
                            sparseTileCount++;
                        }
                        if (metrics.terracedAreaShare > 0.30f)
                        {
                            denseTileCount++;
                        }

                        if (!firstTileEntry)
                        {
                            tileJson.Append(",\n");
                        }
                        firstTileEntry = false;
                        AppendTileJson(tileJson, tile, rollIndex, styleName, metrics);
                    }
                }

                rollHasSparseAndDenseTile[rollIndex] = sparseTileCount > 0 && denseTileCount > 0;
                rollHasBothKinds[rollIndex] = aggregate.lowOutline > BothKindsMinimumMetres
                    && aggregate.totalOutline - aggregate.lowOutline > BothKindsMinimumMetres;

                DrawText(sheet, sheetWidth, sheetHeight, rollIndex * (BlockPixels + GutterPixels) + 4, 2, "ROLL " + rollIndex.ToString(CultureInfo.InvariantCulture), 2);
            }
        }
        finally
        {
            foreach (TerrainTileData data in createdData)
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        Texture2D texture = new Texture2D(sheetWidth, sheetHeight, TextureFormat.RGBA32, false);
        byte[] pngBytes;
        try
        {
            // Sheet rows are stored top-down; Texture2D row 0 is the bottom.
            Color32[] flipped = new Color32[sheet.Length];
            for (int rowFromTop = 0; rowFromTop < sheetHeight; rowFromTop++)
            {
                Array.Copy(sheet, rowFromTop * sheetWidth, flipped, (sheetHeight - 1 - rowFromTop) * sheetWidth, sheetWidth);
            }
            texture.SetPixels32(flipped);
            pngBytes = texture.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        string seedText = worldSeed.ToString(CultureInfo.InvariantCulture);
        string pngPath = outputFolder.TrimEnd('/', '\\') + "/plateau_plan_sheet_seed" + seedText + ".png";
        string jsonPath = outputFolder.TrimEnd('/', '\\') + "/plateau_plan_metrics_seed" + seedText + ".json";
        Directory.CreateDirectory(outputFolder);

        string jsonText;
        string summary = BuildAggregateJson(worldSeed, rollAggregates, rollHasSparseAndDenseTile, rollHasBothKinds, tileJson.ToString(), out jsonText);
        File.WriteAllBytes(pngPath, pngBytes);
        File.WriteAllText(jsonPath, jsonText, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(pngPath);
        AssetDatabase.ImportAsset(jsonPath);
        stopwatch.Stop();
        return summary + " png=" + pngPath + " json=" + jsonPath + "\nelapsedMs=" + stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
    }

    private static void PaintTile(Color32[] sheet, int sheetWidth, int sheetHeight, int tileOriginX, int tileOriginTop, TerrainTileData data,
        Func<Vector2Int, TerrainTileData> lookup, int maxLevel, float cornerRadiusMetres)
    {
        float[][] signedDistances = new float[maxLevel + 1][];
        for (int threshold = 1; threshold <= maxLevel; threshold++)
        {
            signedDistances[threshold] = TerraceLevelField.BuildSignedDistance(data, threshold, cornerRadiusMetres, lookup, PixelsPerMetre);
        }

        for (int pixelZ = 0; pixelZ < TilePixels; pixelZ++)
        {
            int sheetRow = tileOriginTop + (TilePixels - 1 - pixelZ);
            for (int pixelX = 0; pixelX < TilePixels; pixelX++)
            {
                int cellIndex = (pixelZ / PixelsPerMetre) * CellsPerSide + pixelX / PixelsPerMetre;
                int level = data.baseTerraceLevels[cellIndex] - TerrainTileData.TerraceLevelBias;
                Color32 colour = ColourForCell(level, data.baseTerraceStepKinds[cellIndex]);

                if (pixelX == 0 || pixelZ == 0)
                {
                    colour = OutlineColour;
                }
                else
                {
                    for (int threshold = 1; threshold <= maxLevel; threshold++)
                    {
                        float signedDistance = signedDistances[threshold][pixelZ * SignedDistanceSide + pixelX];
                        if (Mathf.Abs(signedDistance) < OutlineHalfWidthMetres)
                        {
                            colour = OutlineColour;
                            break;
                        }
                    }
                }
                SetSheetPixel(sheet, sheetWidth, sheetHeight, tileOriginX + pixelX, sheetRow, colour);
            }
        }
    }

    private static Color32 ColourForCell(int level, byte stepKinds)
    {
        if (level <= 0)
        {
            return LevelZeroColour;
        }
        bool isLow = TerrainTileData.IsHillStep(stepKinds, level);
        if (level == 1)
        {
            return isLow ? LevelOneLowColour : LevelOneTallColour;
        }
        return isLow ? LevelTwoLowColour : LevelTwoTallColour;
    }

    private static void SetSheetPixel(Color32[] sheet, int sheetWidth, int sheetHeight, int pixelX, int rowFromTop, Color32 colour)
    {
        if (pixelX < 0 || pixelX >= sheetWidth || rowFromTop < 0 || rowFromTop >= sheetHeight)
        {
            return;
        }
        sheet[rowFromTop * sheetWidth + pixelX] = colour;
    }

    private static void DrawText(Color32[] sheet, int sheetWidth, int sheetHeight, int originX, int originTop, string text, int scale)
    {
        string upper = text.ToUpperInvariant();
        int advance = 4 * scale;
        for (int backingX = -1; backingX < upper.Length * advance; backingX++)
        {
            for (int backingY = -1; backingY < 5 * scale + 1; backingY++)
            {
                SetSheetPixel(sheet, sheetWidth, sheetHeight, originX + backingX, originTop + backingY, BackgroundColour);
            }
        }
        for (int glyphPosition = 0; glyphPosition < upper.Length; glyphPosition++)
        {
            int glyphIndex = GlyphCharacters.IndexOf(upper[glyphPosition]);
            if (glyphIndex < 0)
            {
                continue;
            }
            ushort bits = GlyphBits[glyphIndex];
            for (int glyphRow = 0; glyphRow < 5; glyphRow++)
            {
                for (int glyphColumn = 0; glyphColumn < 3; glyphColumn++)
                {
                    int bitIndex = 14 - (glyphRow * 3 + glyphColumn);
                    if (((bits >> bitIndex) & 1) == 0)
                    {
                        continue;
                    }
                    for (int scaleX = 0; scaleX < scale; scaleX++)
                    {
                        for (int scaleY = 0; scaleY < scale; scaleY++)
                        {
                            SetSheetPixel(sheet, sheetWidth, sheetHeight,
                                originX + glyphPosition * advance + glyphColumn * scale + scaleX,
                                originTop + glyphRow * scale + scaleY, TextColour);
                        }
                    }
                }
            }
        }
    }

    private static void AccumulateMetrics(RollAggregate aggregate, TerracePlateauTileMetrics metrics)
    {
        aggregate.totalOutline += metrics.totalOutlineMetres;
        aggregate.axisAlignedOutline += metrics.axisAlignedOutlineShare * metrics.totalOutlineMetres;
        aggregate.lowOutline += metrics.lowStepOutlineMetres;
        aggregate.longestStraight = Mathf.Max(aggregate.longestStraight, metrics.longestStraightStretchMetres);
        aggregate.neckViolationCells += metrics.neckOrGapViolationCells;
        aggregate.minTerracedShare = Mathf.Min(aggregate.minTerracedShare, metrics.terracedAreaShare);
        aggregate.maxTerracedShare = Mathf.Max(aggregate.maxTerracedShare, metrics.terracedAreaShare);
        if (metrics.regionCompactness != null)
        {
            foreach (float compactness in metrics.regionCompactness)
            {
                aggregate.minCompactness = Mathf.Min(aggregate.minCompactness, compactness);
                aggregate.maxCompactness = Mathf.Max(aggregate.maxCompactness, compactness);
                aggregate.compactnessCount++;
            }
        }
    }

    private static string Number(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string Flag(bool value)
    {
        return value ? "true" : "false";
    }

    private static void AppendTileJson(StringBuilder builder, Vector2Int tile, int rollIndex, string styleName, TerracePlateauTileMetrics metrics)
    {
        float minCompactness = 0f;
        float maxCompactness = 0f;
        int compactnessCount = 0;
        if (metrics.regionCompactness != null && metrics.regionCompactness.Count > 0)
        {
            minCompactness = float.MaxValue;
            maxCompactness = float.MinValue;
            foreach (float compactness in metrics.regionCompactness)
            {
                minCompactness = Mathf.Min(minCompactness, compactness);
                maxCompactness = Mathf.Max(maxCompactness, compactness);
                compactnessCount++;
            }
        }
        builder.Append("    {\"tile\": [").Append(tile.x).Append(", ").Append(tile.y).Append("], \"roll\": ").Append(rollIndex)
            .Append(", \"style\": \"").Append(styleName).Append("\"")
            .Append(", \"totalOutlineMetres\": ").Append(Number(metrics.totalOutlineMetres))
            .Append(", \"axisAlignedOutlineShare\": ").Append(Number(metrics.axisAlignedOutlineShare))
            .Append(", \"longestStraightStretchMetres\": ").Append(Number(metrics.longestStraightStretchMetres))
            .Append(", \"compactness\": {\"min\": ").Append(Number(minCompactness)).Append(", \"max\": ").Append(Number(maxCompactness))
            .Append(", \"count\": ").Append(compactnessCount).Append("}")
            .Append(", \"neckOrGapViolationCells\": ").Append(metrics.neckOrGapViolationCells)
            .Append(", \"terracedAreaShare\": ").Append(Number(metrics.terracedAreaShare))
            .Append(", \"lowStepOutlineMetres\": ").Append(Number(metrics.lowStepOutlineMetres))
            .Append("}");
    }

    private static string BuildAggregateJson(int worldSeed, RollAggregate[] rollAggregates, bool[] rollHasSparseAndDenseTile,
        bool[] rollHasBothKinds, string tileJson, out string jsonText)
    {
        RollAggregate overall = new RollAggregate();
        bool everyRollSparseAndDense = true;
        bool everyRollBothKinds = true;
        StringBuilder rollJson = new StringBuilder();
        for (int rollIndex = 0; rollIndex < rollAggregates.Length; rollIndex++)
        {
            RollAggregate roll = rollAggregates[rollIndex];
            overall.totalOutline += roll.totalOutline;
            overall.axisAlignedOutline += roll.axisAlignedOutline;
            overall.lowOutline += roll.lowOutline;
            overall.longestStraight = Mathf.Max(overall.longestStraight, roll.longestStraight);
            overall.worstStraightExcessOverTileLimit = Mathf.Max(overall.worstStraightExcessOverTileLimit, roll.worstStraightExcessOverTileLimit);
            overall.neckViolationCells += roll.neckViolationCells;
            overall.minCompactness = Mathf.Min(overall.minCompactness, roll.minCompactness);
            overall.maxCompactness = Mathf.Max(overall.maxCompactness, roll.maxCompactness);
            overall.compactnessCount += roll.compactnessCount;
            overall.minTerracedShare = Mathf.Min(overall.minTerracedShare, roll.minTerracedShare);
            overall.maxTerracedShare = Mathf.Max(overall.maxTerracedShare, roll.maxTerracedShare);
            everyRollSparseAndDense &= rollHasSparseAndDenseTile[rollIndex];
            everyRollBothKinds &= rollHasBothKinds[rollIndex];

            if (rollIndex > 0)
            {
                rollJson.Append(",\n");
            }
            rollJson.Append("    {\"roll\": ").Append(rollIndex)
                .Append(", \"M1\": ").Append(Number(Ratio(roll.axisAlignedOutline, roll.totalOutline)))
                .Append(", \"M2\": ").Append(Number(roll.longestStraight))
                .Append(", \"M3\": {\"min\": ").Append(Number(roll.compactnessCount > 0 ? roll.minCompactness : 0f))
                .Append(", \"max\": ").Append(Number(roll.compactnessCount > 0 ? roll.maxCompactness : 0f)).Append("}")
                .Append(", \"M4\": ").Append(roll.neckViolationCells)
                .Append(", \"M5\": {\"minTileShare\": ").Append(Number(roll.minTerracedShare))
                .Append(", \"maxTileShare\": ").Append(Number(roll.maxTerracedShare))
                .Append(", \"hasTileUnder10AndOver30\": ").Append(Flag(rollHasSparseAndDenseTile[rollIndex])).Append("}")
                .Append(", \"M6\": {\"lowShare\": ").Append(Number(Ratio(roll.lowOutline, roll.totalOutline)))
                .Append(", \"bothKindsOccur\": ").Append(Flag(rollHasBothKinds[rollIndex])).Append("}}");
        }

        float m1 = Ratio(overall.axisAlignedOutline, overall.totalOutline);
        float m6 = Ratio(overall.lowOutline, overall.totalOutline);
        float minCompactness = overall.compactnessCount > 0 ? overall.minCompactness : 0f;
        float maxCompactness = overall.compactnessCount > 0 ? overall.maxCompactness : 0f;
        bool passM1 = m1 < 0.25f;
        bool passM2 = overall.worstStraightExcessOverTileLimit <= 0f;
        bool passM3 = minCompactness < 0.35f && maxCompactness > 0.6f;
        bool passM4 = overall.neckViolationCells == 0;
        bool passM5 = overall.minTerracedShare >= 0f && overall.maxTerracedShare <= 0.55f && everyRollSparseAndDense;
        bool passM6 = m6 >= 0.4f && everyRollBothKinds;

        StringBuilder builder = new StringBuilder();
        builder.Append("{\n  \"seed\": ").Append(worldSeed).Append(",\n  \"tiles\": [\n").Append(tileJson).Append("\n  ],\n  \"rolls\": [\n")
            .Append(rollJson).Append("\n  ],\n  \"overall\": {\n")
            .Append("    \"M1\": ").Append(Number(m1)).Append(",\n")
            .Append("    \"M2\": ").Append(Number(overall.longestStraight)).Append(",\n")
            .Append("    \"M3\": {\"min\": ").Append(Number(minCompactness)).Append(", \"max\": ").Append(Number(maxCompactness)).Append("},\n")
            .Append("    \"M4\": ").Append(overall.neckViolationCells).Append(",\n")
            .Append("    \"M5\": {\"minTileShare\": ").Append(Number(overall.minTerracedShare)).Append(", \"maxTileShare\": ").Append(Number(overall.maxTerracedShare))
            .Append(", \"everyRollHasTileUnder10AndOver30\": ").Append(Flag(everyRollSparseAndDense)).Append("},\n")
            .Append("    \"M6\": {\"lowShare\": ").Append(Number(m6)).Append(", \"everyRollBothKindsOccur\": ").Append(Flag(everyRollBothKinds)).Append("}\n  },\n")
            .Append("  \"pass\": {\"M1\": ").Append(Flag(passM1)).Append(", \"M2\": ").Append(Flag(passM2)).Append(", \"M3\": ").Append(Flag(passM3))
            .Append(", \"M4\": ").Append(Flag(passM4)).Append(", \"M5\": ").Append(Flag(passM5)).Append(", \"M6\": ").Append(Flag(passM6)).Append("}\n}\n");
        jsonText = builder.ToString();

        return "Plateau plan seed " + worldSeed + ": M1=" + Number(m1) + (passM1 ? " ok" : " FAIL")
            + " M2=" + Number(overall.longestStraight) + (passM2 ? " ok" : " FAIL")
            + " M3=" + Number(minCompactness) + ".." + Number(maxCompactness) + (passM3 ? " ok" : " FAIL")
            + " M4=" + overall.neckViolationCells + (passM4 ? " ok" : " FAIL")
            + " M5=" + Number(overall.minTerracedShare) + ".." + Number(overall.maxTerracedShare) + (passM5 ? " ok" : " FAIL")
            + " M6=" + Number(m6) + (passM6 ? " ok" : " FAIL");
    }

    private static float Ratio(float numerator, float denominator)
    {
        return denominator > 0f ? numerator / denominator : 0f;
    }
}
#endif
