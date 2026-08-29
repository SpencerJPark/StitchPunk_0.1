#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DotsMovementToolkit
{
    /// <summary>
    /// Renders the live nav-grid cost map as a single vertex-coloured mesh: one tile per cell,
    /// tinted by traversal cost, with cells that changed in the last cost-map rebuild flashed so
    /// moving obstacles are visible as they land. Drawn with Graphics.RenderMesh, so it appears in
    /// both the Game and Scene views without touching anything the game itself renders.
    /// </summary>
    // OrderLast so it reads the cost map after NavGridSystem (OrderFirst) has rebuilt it this frame.
    [UpdateInGroup(typeof(MovementCoordinatorSystemGroup), OrderLast = true)]
    public partial class NavGridDebugRenderSystem : SystemBase
    {
        private const string DebugShaderName = "Hidden/DotsMovementToolkit/NavGridDebug";

        // Outlines share the fill's corner positions but get their own vertex block, so they can
        // carry a brighter, more opaque colour — one material, two submeshes, one draw call each.
        private const float OutlineAlphaBoost = 3f;
        private const float OutlineBrightnessBoost = 1.4f;
        private const int VerticesPerQuad = 4;
        private const int FillIndicesPerQuad = 6;
        private const int OutlineIndicesPerQuad = 8;

        private Mesh debugMesh;
        private Material debugMaterial;
        private bool hasReportedMissingShader;
        private bool hasReportedCellBudgetDowngrade;

        private NativeArray<byte> previousCosts;
        private NativeArray<double> cellLastChangedTime;
        private bool hasCostBaseline;
        private uint lastSeenCostMapVersion;
        private double changeHighlightExpiryTime;
        private bool wasChangeHighlightActive;

        private readonly List<int> drawnCellIndices = new List<int>();
        private Vector3[] meshVertices = System.Array.Empty<Vector3>();
        private Color[] meshColors = System.Array.Empty<Color>();
        private int[] fillIndices = System.Array.Empty<int>();
        private int[] outlineIndices = System.Array.Empty<int>();

        private bool hasBuiltGeometry;
        private bool outlinesInCurrentMesh;
        private NavGridDebugSettings settingsGeometryWasBuiltWith;
        private NavGridConfig configGeometryWasBuiltWith;
        private NavGridDebugSettings settingsColoursWereBuiltWith;

        protected override void OnCreate()
        {
            RequireForUpdate<NavGridDebugSettings>();
        }

        protected override void OnUpdate()
        {
            NavGridDebugSettings debugSettings = SystemAPI.GetSingleton<NavGridDebugSettings>();

            if (debugSettings.displayMode == NavGridDebugDisplayMode.Off)
            {
                ReleaseGeometry();
                return;
            }

            if (!SystemAPI.TryGetSingleton(out NavGridSettings gridSettings)) return;
            if (!SystemAPI.TryGetSingleton(out NavGridConfig gridConfig)) return;
            if (!SystemAPI.TryGetSingleton(out NavGridCostMap costMap)) return;
            if (!costMap.costs.IsCreated) return;
            if (!TryEnsureMaterial()) return;

            // costs is a raw NativeArray inside a component, so ECS job safety does not cover it.
            // NavGridSystem already syncs after its rebuild; this is the belt to that braces.
            CompleteDependency();

            double now = SystemAPI.Time.ElapsedTime;
            bool costsChanged = costMap.costMapVersion != lastSeenCostMapVersion;
            if (costsChanged)
            {
                lastSeenCostMapVersion = costMap.costMapVersion;
                RecordChangedCells(costMap.costs, debugSettings, now);
            }

            // The frame a flash expires needs a full rebuild, not just a recolour: it both settles
            // the fade (otherwise the last colours drawn are stuck part-way) and, in ObstaclesOnly,
            // drops the cells that were only being drawn *because* they were flashing.
            bool changeHighlightActive = now < changeHighlightExpiryTime;
            bool changeHighlightJustEnded = !changeHighlightActive && wasChangeHighlightActive;
            wasChangeHighlightActive = changeHighlightActive;

            bool geometryIsStale = !hasBuiltGeometry
                || costsChanged
                || changeHighlightJustEnded
                || !GeometryInputsMatch(debugSettings, settingsGeometryWasBuiltWith)
                || !GridInputsMatch(gridConfig, configGeometryWasBuiltWith);

            if (geometryIsStale)
            {
                RebuildGeometry(gridConfig, gridSettings, debugSettings, costMap.costs, now);
                settingsGeometryWasBuiltWith = debugSettings;
                configGeometryWasBuiltWith = gridConfig;
                hasBuiltGeometry = true;
            }
            else if (changeHighlightActive || !ColourInputsMatch(debugSettings, settingsColoursWereBuiltWith))
            {
                RefreshColours(gridSettings, debugSettings, costMap.costs, now);
            }

            if (drawnCellIndices.Count == 0) return;

            RenderParams renderParams = new RenderParams(debugMaterial)
            {
                worldBounds = debugMesh.bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = 0
            };

            Graphics.RenderMesh(renderParams, debugMesh, 0, Matrix4x4.identity);
            if (outlinesInCurrentMesh)
                Graphics.RenderMesh(renderParams, debugMesh, 1, Matrix4x4.identity);
        }

        // Stamps "changed at" times so RefreshColours can fade a freshly blocked (or freshly freed)
        // cell back to its resting colour. The first sync only seeds the baseline — otherwise the
        // initial fill of the cost map would light up the entire grid.
        private void RecordChangedCells(NativeArray<byte> costs, NavGridDebugSettings debugSettings, double now)
        {
            if (debugSettings.changeHighlightSeconds <= 0f)
            {
                DisposeChangeTracking();
                return;
            }

            if (!previousCosts.IsCreated || previousCosts.Length != costs.Length)
            {
                DisposeChangeTracking();
                previousCosts = new NativeArray<byte>(costs.Length, Allocator.Persistent);
                cellLastChangedTime = new NativeArray<double>(costs.Length, Allocator.Persistent);
                hasCostBaseline = false;
            }

            if (!hasCostBaseline)
            {
                previousCosts.CopyFrom(costs);
                hasCostBaseline = true;
                return;
            }

            bool anyCellChanged = false;
            for (int cellIndex = 0; cellIndex < costs.Length; cellIndex++)
            {
                if (previousCosts[cellIndex] == costs[cellIndex]) continue;
                previousCosts[cellIndex] = costs[cellIndex];
                cellLastChangedTime[cellIndex] = now;
                anyCellChanged = true;
            }

            if (anyCellChanged)
                changeHighlightExpiryTime = now + debugSettings.changeHighlightSeconds;
        }

        private void RebuildGeometry(
            NavGridConfig gridConfig,
            NavGridSettings gridSettings,
            NavGridDebugSettings debugSettings,
            NativeArray<byte> costs,
            double now)
        {
            NavGridDebugDisplayMode effectiveMode = ResolveModeWithinCellBudget(gridConfig, debugSettings);

            int firstLayer = debugSettings.layerToDraw < 0
                ? 0
                : math.clamp(debugSettings.layerToDraw, 0, gridConfig.layerCount - 1);
            int lastLayer = debugSettings.layerToDraw < 0 ? gridConfig.layerCount - 1 : firstLayer;
            int cellsPerLayer = gridConfig.width * gridConfig.height;
            int cellBudget = math.max(1, debugSettings.maxDrawnCells);

            drawnCellIndices.Clear();
            for (int layer = firstLayer; layer <= lastLayer && drawnCellIndices.Count < cellBudget; layer++)
            {
                for (int localIndex = 0; localIndex < cellsPerLayer; localIndex++)
                {
                    int cellIndex = layer * cellsPerLayer + localIndex;
                    if (cellIndex >= costs.Length) break;
                    if (!ShouldDrawCell(costs[cellIndex], cellIndex, gridSettings, debugSettings, effectiveMode, now)) continue;

                    drawnCellIndices.Add(cellIndex);
                    if (drawnCellIndices.Count >= cellBudget) break;
                }
            }

            outlinesInCurrentMesh = debugSettings.drawCellOutlines;
            int drawnCells = drawnCellIndices.Count;
            EnsureMesh();
            EnsureMeshBuffers(drawnCells, outlinesInCurrentMesh);

            if (drawnCells == 0)
            {
                debugMesh.Clear();
                return;
            }

            float paddedHalfExtent = gridConfig.cellSize * (0.5f - math.clamp(debugSettings.cellPadding, 0f, 0.45f));
            int outlineVertexBase = drawnCells * VerticesPerQuad;

            for (int drawIndex = 0; drawIndex < drawnCells; drawIndex++)
            {
                int cellIndex = drawnCellIndices[drawIndex];
                NavGridSystem.GetPositionFromIndex(cellIndex, gridConfig.width, gridConfig.height, out int2 cell, out int layer);

                float3 cellCentre = NavGridSystem.GetWorldCenterPosition(
                    cell.x, cell.y, layer, gridConfig.cellSize, gridConfig.layerHeight);
                float tileHeight = cellCentre.y + debugSettings.heightOffset;

                int fillVertex = drawIndex * VerticesPerQuad;
                meshVertices[fillVertex + 0] = new Vector3(cellCentre.x - paddedHalfExtent, tileHeight, cellCentre.z - paddedHalfExtent);
                meshVertices[fillVertex + 1] = new Vector3(cellCentre.x + paddedHalfExtent, tileHeight, cellCentre.z - paddedHalfExtent);
                meshVertices[fillVertex + 2] = new Vector3(cellCentre.x + paddedHalfExtent, tileHeight, cellCentre.z + paddedHalfExtent);
                meshVertices[fillVertex + 3] = new Vector3(cellCentre.x - paddedHalfExtent, tileHeight, cellCentre.z + paddedHalfExtent);

                int triangleStart = drawIndex * FillIndicesPerQuad;
                fillIndices[triangleStart + 0] = fillVertex + 0;
                fillIndices[triangleStart + 1] = fillVertex + 2;
                fillIndices[triangleStart + 2] = fillVertex + 1;
                fillIndices[triangleStart + 3] = fillVertex + 0;
                fillIndices[triangleStart + 4] = fillVertex + 3;
                fillIndices[triangleStart + 5] = fillVertex + 2;

                if (!outlinesInCurrentMesh) continue;

                int outlineVertex = outlineVertexBase + fillVertex;
                meshVertices[outlineVertex + 0] = meshVertices[fillVertex + 0];
                meshVertices[outlineVertex + 1] = meshVertices[fillVertex + 1];
                meshVertices[outlineVertex + 2] = meshVertices[fillVertex + 2];
                meshVertices[outlineVertex + 3] = meshVertices[fillVertex + 3];

                int lineStart = drawIndex * OutlineIndicesPerQuad;
                outlineIndices[lineStart + 0] = outlineVertex + 0;
                outlineIndices[lineStart + 1] = outlineVertex + 1;
                outlineIndices[lineStart + 2] = outlineVertex + 1;
                outlineIndices[lineStart + 3] = outlineVertex + 2;
                outlineIndices[lineStart + 4] = outlineVertex + 2;
                outlineIndices[lineStart + 5] = outlineVertex + 3;
                outlineIndices[lineStart + 6] = outlineVertex + 3;
                outlineIndices[lineStart + 7] = outlineVertex + 0;
            }

            FillColours(gridSettings, debugSettings, costs, now);

            debugMesh.Clear();
            debugMesh.indexFormat = IndexFormat.UInt32;
            debugMesh.SetVertices(meshVertices);
            debugMesh.SetColors(meshColors);
            debugMesh.subMeshCount = outlinesInCurrentMesh ? 2 : 1;
            debugMesh.SetIndices(fillIndices, MeshTopology.Triangles, 0, true);
            if (outlinesInCurrentMesh)
                debugMesh.SetIndices(outlineIndices, MeshTopology.Lines, 1, false);
            debugMesh.RecalculateBounds();
            settingsColoursWereBuiltWith = debugSettings;
        }

        private void RefreshColours(
            NavGridSettings gridSettings,
            NavGridDebugSettings debugSettings,
            NativeArray<byte> costs,
            double now)
        {
            if (drawnCellIndices.Count == 0 || debugMesh == null) return;
            FillColours(gridSettings, debugSettings, costs, now);
            debugMesh.SetColors(meshColors);
            settingsColoursWereBuiltWith = debugSettings;
        }

        private void FillColours(
            NavGridSettings gridSettings,
            NavGridDebugSettings debugSettings,
            NativeArray<byte> costs,
            double now)
        {
            int drawnCells = drawnCellIndices.Count;
            int outlineVertexBase = drawnCells * VerticesPerQuad;

            for (int drawIndex = 0; drawIndex < drawnCells; drawIndex++)
            {
                int cellIndex = drawnCellIndices[drawIndex];
                float4 cellColour = ResolveCellColour(costs[cellIndex], cellIndex, gridSettings, debugSettings, now);

                Color fillColour = new Color(cellColour.x, cellColour.y, cellColour.z, cellColour.w);
                Color outlineColour = new Color(
                    math.min(1f, cellColour.x * OutlineBrightnessBoost),
                    math.min(1f, cellColour.y * OutlineBrightnessBoost),
                    math.min(1f, cellColour.z * OutlineBrightnessBoost),
                    math.min(1f, cellColour.w * OutlineAlphaBoost));

                int fillVertex = drawIndex * VerticesPerQuad;
                for (int corner = 0; corner < VerticesPerQuad; corner++)
                {
                    meshColors[fillVertex + corner] = fillColour;
                    if (outlinesInCurrentMesh)
                        meshColors[outlineVertexBase + fillVertex + corner] = outlineColour;
                }
            }
        }

        private float4 ResolveCellColour(
            byte cost,
            int cellIndex,
            NavGridSettings gridSettings,
            NavGridDebugSettings debugSettings,
            double now)
        {
            float4 restingColour;
            if (cost >= gridSettings.wallCost)
            {
                restingColour = debugSettings.blockedColor;
            }
            else if (cost <= gridSettings.defaultCost)
            {
                restingColour = debugSettings.walkableColor;
            }
            else
            {
                // Anything between default and wall is "discouraged" — ramp toward the discouraged
                // colour so a cost map with several tiers stays readable, not just free/heavy/wall.
                float discouragedSpan = math.max(1f, gridSettings.heavyCost - (float)gridSettings.defaultCost);
                float discouragedFraction = math.saturate((cost - gridSettings.defaultCost) / discouragedSpan);
                restingColour = math.lerp(debugSettings.walkableColor, debugSettings.discouragedColor, discouragedFraction);
            }

            float highlightAge = ChangeHighlightAge(cellIndex, debugSettings, now);
            if (highlightAge < 0f) return restingColour;

            float fadeFraction = math.saturate(highlightAge / debugSettings.changeHighlightSeconds);
            return math.lerp(debugSettings.recentlyChangedColor, restingColour, fadeFraction);
        }

        /// <summary>Seconds since this cell's cost last changed, or -1 when it is not currently highlighted.</summary>
        private float ChangeHighlightAge(int cellIndex, NavGridDebugSettings debugSettings, double now)
        {
            if (debugSettings.changeHighlightSeconds <= 0f) return -1f;
            if (!cellLastChangedTime.IsCreated || cellIndex >= cellLastChangedTime.Length) return -1f;

            double changedAt = cellLastChangedTime[cellIndex];
            if (changedAt <= 0d) return -1f;

            float age = (float)(now - changedAt);
            return age < debugSettings.changeHighlightSeconds ? age : -1f;
        }

        private bool ShouldDrawCell(
            byte cost,
            int cellIndex,
            NavGridSettings gridSettings,
            NavGridDebugSettings debugSettings,
            NavGridDebugDisplayMode effectiveMode,
            double now)
        {
            if (effectiveMode == NavGridDebugDisplayMode.FullGrid) return true;
            if (cost != gridSettings.defaultCost) return true;
            // A cell that just *stopped* being an obstacle is the interesting half of a live update,
            // so keep drawing it for as long as its change highlight lasts.
            return ChangeHighlightAge(cellIndex, debugSettings, now) >= 0f;
        }

        private NavGridDebugDisplayMode ResolveModeWithinCellBudget(NavGridConfig gridConfig, NavGridDebugSettings debugSettings)
        {
            if (debugSettings.displayMode != NavGridDebugDisplayMode.FullGrid) return debugSettings.displayMode;

            int layersDrawn = debugSettings.layerToDraw < 0 ? gridConfig.layerCount : 1;
            int candidateCells = gridConfig.width * gridConfig.height * layersDrawn;
            if (candidateCells <= debugSettings.maxDrawnCells) return NavGridDebugDisplayMode.FullGrid;

            if (!hasReportedCellBudgetDowngrade)
            {
                hasReportedCellBudgetDowngrade = true;
                Debug.LogWarning(
                    $"NavGrid debug view: FullGrid would draw {candidateCells} cells, over the maxDrawnCells " +
                    $"budget of {debugSettings.maxDrawnCells}. Falling back to ObstaclesOnly. Raise the budget " +
                    "on NavGridAuthoring, or pick a single layer, if you need the whole grid.");
            }
            return NavGridDebugDisplayMode.ObstaclesOnly;
        }

        private void EnsureMeshBuffers(int drawnCells, bool withOutlines)
        {
            int requiredVertices = drawnCells * VerticesPerQuad * (withOutlines ? 2 : 1);
            if (meshVertices.Length != requiredVertices)
            {
                meshVertices = new Vector3[requiredVertices];
                meshColors = new Color[requiredVertices];
            }

            int requiredFillIndices = drawnCells * FillIndicesPerQuad;
            if (fillIndices.Length != requiredFillIndices)
                fillIndices = new int[requiredFillIndices];

            int requiredOutlineIndices = withOutlines ? drawnCells * OutlineIndicesPerQuad : 0;
            if (outlineIndices.Length != requiredOutlineIndices)
                outlineIndices = new int[requiredOutlineIndices];
        }

        private void EnsureMesh()
        {
            if (debugMesh != null) return;
            debugMesh = new Mesh { name = "NavGridDebug", hideFlags = HideFlags.HideAndDontSave };
            debugMesh.MarkDynamic();
        }

        private bool TryEnsureMaterial()
        {
            if (debugMaterial != null) return true;

            Shader debugShader = Shader.Find(DebugShaderName);
            if (debugShader == null)
            {
                if (!hasReportedMissingShader)
                {
                    hasReportedMissingShader = true;
                    Debug.LogWarning(
                        $"NavGrid debug view disabled: shader '{DebugShaderName}' was not found. In a player " +
                        "build it must be listed under Project Settings > Graphics > Always Included Shaders.");
                }
                return false;
            }

            debugMaterial = new Material(debugShader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        private void ReleaseGeometry()
        {
            if (!hasBuiltGeometry) return;
            hasBuiltGeometry = false;
            drawnCellIndices.Clear();
            if (debugMesh != null) debugMesh.Clear();
            DisposeChangeTracking();
        }

        private void DisposeChangeTracking()
        {
            if (previousCosts.IsCreated) previousCosts.Dispose();
            if (cellLastChangedTime.IsCreated) cellLastChangedTime.Dispose();
            hasCostBaseline = false;
            changeHighlightExpiryTime = 0d;
            wasChangeHighlightActive = false;
        }

        protected override void OnDestroy()
        {
            DisposeChangeTracking();
            DestroyDebugObject(debugMesh);
            DestroyDebugObject(debugMaterial);
            debugMesh = null;
            debugMaterial = null;
        }

        private static void DestroyDebugObject(Object debugObject)
        {
            if (debugObject == null) return;
            if (Application.isPlaying) Object.Destroy(debugObject);
            else Object.DestroyImmediate(debugObject);
        }

        private static bool GeometryInputsMatch(in NavGridDebugSettings left, in NavGridDebugSettings right)
        {
            return left.displayMode == right.displayMode
                && left.layerToDraw == right.layerToDraw
                && left.drawCellOutlines == right.drawCellOutlines
                && left.maxDrawnCells == right.maxDrawnCells
                && left.heightOffset.Equals(right.heightOffset)
                && left.cellPadding.Equals(right.cellPadding)
                && left.changeHighlightSeconds.Equals(right.changeHighlightSeconds);
        }

        private static bool ColourInputsMatch(in NavGridDebugSettings left, in NavGridDebugSettings right)
        {
            return left.walkableColor.Equals(right.walkableColor)
                && left.discouragedColor.Equals(right.discouragedColor)
                && left.blockedColor.Equals(right.blockedColor)
                && left.recentlyChangedColor.Equals(right.recentlyChangedColor);
        }

        private static bool GridInputsMatch(in NavGridConfig left, in NavGridConfig right)
        {
            return left.width == right.width
                && left.height == right.height
                && left.layerCount == right.layerCount
                && left.cellSize.Equals(right.cellSize)
                && left.layerHeight.Equals(right.layerHeight);
        }
    }
}
#endif
