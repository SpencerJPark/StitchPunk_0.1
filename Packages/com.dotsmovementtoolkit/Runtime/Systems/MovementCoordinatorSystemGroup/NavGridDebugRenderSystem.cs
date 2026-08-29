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
    /// Renders the live nav-grid cost map as a single vertex-coloured mesh: walkable cells as flat
    /// tiles, blocked and discouraged cells extruded into solid blocks, and any cell whose cost
    /// changed in the last rebuild flashed so moving obstacles are visible as they land. Drawn with
    /// Graphics.RenderMesh, so it appears in both the Game and Scene views without touching anything
    /// the game itself renders.
    /// </summary>
    // OrderLast so it reads the cost map after NavGridSystem (OrderFirst) has rebuilt it this frame.
    [UpdateInGroup(typeof(MovementCoordinatorSystemGroup), OrderLast = true)]
    public partial class NavGridDebugRenderSystem : SystemBase
    {
        private const string DebugShaderName = "Hidden/DotsMovementToolkit/NavGridDebug";

        // Outlines share the fill's corner positions but get their own vertex block, so they can
        // carry a brighter, more opaque colour — one material, two submeshes, one draw call each.
        private const float OutlineAlphaBoost = 2.5f;
        private const float OutlineBrightnessBoost = 1.4f;

        // Extruded sides are shaded down so a block reads as a volume rather than a flat silhouette.
        private const float ExtrudedSideShade = 0.7f;
        private const float DiscouragedExtrusionFraction = 0.5f;

        private const int VerticesPerQuad = 4;

        // maxDrawnCells budgets *cells*, but an extruded cell costs five quads. This is the backstop
        // that keeps a grid of nothing but walls from building a million-vertex mesh.
        private const int MaxQuads = 60000;

        private Mesh debugMesh;
        private Material debugMaterial;
        private bool hasReportedMissingShader;
        private bool hasReportedCellBudgetDowngrade;
        private bool hasReportedQuadCeiling;

        private NativeArray<byte> previousCosts;
        private NativeArray<double> cellLastChangedTime;
        private bool hasCostBaseline;
        private uint lastSeenCostMapVersion;
        private double changeHighlightExpiryTime;
        private bool wasChangeHighlightActive;

        private int lastReportedBlockedCells = -1;
        private int lastReportedDiscouragedCells = -1;

        private readonly List<Vector3> meshVertices = new List<Vector3>();
        private readonly List<Color> meshColors = new List<Color>();
        private readonly List<int> fillIndices = new List<int>();
        private readonly List<int> outlineIndices = new List<int>();

        // One entry per quad, parallel: which cost-map cell it belongs to, and how much to shade it.
        // Recolouring walks these instead of re-deriving geometry.
        private readonly List<int> quadCellIndices = new List<int>();
        private readonly List<float> quadShades = new List<float>();

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
                ReportCostMapContents(gridConfig, gridSettings, costMap.costs);
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

            if (quadCellIndices.Count == 0) return;

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

        // A grid whose footprint misses the level, or obstacles that never reach the physics world,
        // both look identical from the outside: an empty-looking debug view. Say which it is.
        private void ReportCostMapContents(NavGridConfig gridConfig, NavGridSettings gridSettings, NativeArray<byte> costs)
        {
            int blockedCells = 0;
            int discouragedCells = 0;
            for (int cellIndex = 0; cellIndex < costs.Length; cellIndex++)
            {
                byte cost = costs[cellIndex];
                if (cost >= gridSettings.wallCost) blockedCells++;
                else if (cost > gridSettings.defaultCost) discouragedCells++;
            }

            if (blockedCells == lastReportedBlockedCells && discouragedCells == lastReportedDiscouragedCells) return;
            lastReportedBlockedCells = blockedCells;
            lastReportedDiscouragedCells = discouragedCells;

            float3 gridMax = gridConfig.gridOrigin
                + new float3(gridConfig.width * gridConfig.cellSize, 0f, gridConfig.height * gridConfig.cellSize);
            string footprint =
                $"X {gridConfig.gridOrigin.x:0.#}..{gridMax.x:0.#}, Z {gridConfig.gridOrigin.z:0.#}..{gridMax.z:0.#}";

            if (blockedCells + discouragedCells > 0)
            {
                Debug.Log($"NavGrid debug: {blockedCells} blocked + {discouragedCells} discouraged of " +
                          $"{costs.Length} cells. Grid covers {footprint}.");
                return;
            }

            Debug.LogWarning(
                $"NavGrid debug: the cost map has no blocked or discouraged cells at all ({costs.Length} cells, " +
                $"all at defaultCost), so nothing will fill in. The grid covers {footprint} — check that your " +
                "obstacles (1) sit inside that footprint, (2) are baked into a subscene so they exist in the " +
                "physics CollisionWorld at all, and (3) carry a collider whose BelongsTo overlaps wallLayerMask " +
                $"0x{gridSettings.wallLayerMask:X} or heavyLayerMask 0x{gridSettings.heavyLayerMask:X}.");
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

            outlinesInCurrentMesh = debugSettings.drawCellOutlines;
            meshVertices.Clear();
            fillIndices.Clear();
            outlineIndices.Clear();
            quadCellIndices.Clear();
            quadShades.Clear();

            float halfExtent = gridConfig.cellSize * (0.5f - math.clamp(debugSettings.cellPadding, 0f, 0.45f));
            int drawnCells = 0;
            bool hitQuadCeiling = false;

            for (int layer = firstLayer; layer <= lastLayer && drawnCells < cellBudget && !hitQuadCeiling; layer++)
            {
                for (int localIndex = 0; localIndex < cellsPerLayer; localIndex++)
                {
                    int cellIndex = layer * cellsPerLayer + localIndex;
                    if (cellIndex >= costs.Length) break;

                    byte cost = costs[cellIndex];
                    if (!ShouldDrawCell(cost, cellIndex, gridSettings, debugSettings, effectiveMode, now)) continue;

                    AppendCellGeometry(cellIndex, cost, gridConfig, gridSettings, debugSettings, halfExtent);

                    drawnCells++;
                    if (quadCellIndices.Count >= MaxQuads) { hitQuadCeiling = true; break; }
                    if (drawnCells >= cellBudget) break;
                }
            }

            if (hitQuadCeiling && !hasReportedQuadCeiling)
            {
                hasReportedQuadCeiling = true;
                Debug.LogWarning(
                    $"NavGrid debug: hit the internal ceiling of {MaxQuads} quads after {drawnCells} cells and " +
                    "stopped building. Extruded cells cost five quads each — lower maxDrawnCells, switch to " +
                    "ObstaclesOnly, or set the obstacle extrusion height to 0.");
            }

            EnsureMesh();

            int quadCount = quadCellIndices.Count;
            if (quadCount == 0)
            {
                debugMesh.Clear();
                return;
            }

            // The outline block is a positional copy of the fill vertices, so it can carry its own
            // brighter colours while reusing every corner the fill already computed.
            int fillVertexCount = quadCount * VerticesPerQuad;
            if (outlinesInCurrentMesh)
            {
                for (int vertex = 0; vertex < fillVertexCount; vertex++)
                    meshVertices.Add(meshVertices[vertex]);

                for (int quad = 0; quad < quadCount; quad++)
                {
                    int outlineVertex = fillVertexCount + quad * VerticesPerQuad;
                    outlineIndices.Add(outlineVertex + 0); outlineIndices.Add(outlineVertex + 1);
                    outlineIndices.Add(outlineVertex + 1); outlineIndices.Add(outlineVertex + 2);
                    outlineIndices.Add(outlineVertex + 2); outlineIndices.Add(outlineVertex + 3);
                    outlineIndices.Add(outlineVertex + 3); outlineIndices.Add(outlineVertex + 0);
                }
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

        // A walkable cell is one flat tile; anything costlier becomes a block, so obstacles read as
        // volumes from a shallow camera angle instead of paint that the floor hides.
        private void AppendCellGeometry(
            int cellIndex,
            byte cost,
            NavGridConfig gridConfig,
            NavGridSettings gridSettings,
            NavGridDebugSettings debugSettings,
            float halfExtent)
        {
            NavGridSystem.GetPositionFromIndex(cellIndex, gridConfig.width, gridConfig.height, out int2 cell, out int layer);
            float3 cellCentre = NavGridSystem.GetWorldCenterPosition(cell.x, cell.y, layer, gridConfig);

            float floorY = cellCentre.y + debugSettings.heightOffset;
            float extrusion = 0f;
            if (debugSettings.obstacleExtrusionHeight > 0f)
            {
                if (cost >= gridSettings.wallCost) extrusion = debugSettings.obstacleExtrusionHeight;
                else if (cost > gridSettings.defaultCost) extrusion = debugSettings.obstacleExtrusionHeight * DiscouragedExtrusionFraction;
            }
            float topY = floorY + extrusion;

            float xMin = cellCentre.x - halfExtent;
            float xMax = cellCentre.x + halfExtent;
            float zMin = cellCentre.z - halfExtent;
            float zMax = cellCentre.z + halfExtent;

            AppendQuad(
                new Vector3(xMin, topY, zMin), new Vector3(xMax, topY, zMin),
                new Vector3(xMax, topY, zMax), new Vector3(xMin, topY, zMax),
                cellIndex, 1f);

            if (extrusion <= 0f) return;

            AppendQuad(
                new Vector3(xMin, floorY, zMin), new Vector3(xMax, floorY, zMin),
                new Vector3(xMax, topY, zMin), new Vector3(xMin, topY, zMin),
                cellIndex, ExtrudedSideShade);
            AppendQuad(
                new Vector3(xMax, floorY, zMin), new Vector3(xMax, floorY, zMax),
                new Vector3(xMax, topY, zMax), new Vector3(xMax, topY, zMin),
                cellIndex, ExtrudedSideShade);
            AppendQuad(
                new Vector3(xMax, floorY, zMax), new Vector3(xMin, floorY, zMax),
                new Vector3(xMin, topY, zMax), new Vector3(xMax, topY, zMax),
                cellIndex, ExtrudedSideShade);
            AppendQuad(
                new Vector3(xMin, floorY, zMax), new Vector3(xMin, floorY, zMin),
                new Vector3(xMin, topY, zMin), new Vector3(xMin, topY, zMax),
                cellIndex, ExtrudedSideShade);
        }

        private void AppendQuad(Vector3 corner0, Vector3 corner1, Vector3 corner2, Vector3 corner3, int cellIndex, float shade)
        {
            int baseVertex = meshVertices.Count;
            meshVertices.Add(corner0);
            meshVertices.Add(corner1);
            meshVertices.Add(corner2);
            meshVertices.Add(corner3);

            fillIndices.Add(baseVertex + 0); fillIndices.Add(baseVertex + 2); fillIndices.Add(baseVertex + 1);
            fillIndices.Add(baseVertex + 0); fillIndices.Add(baseVertex + 3); fillIndices.Add(baseVertex + 2);

            quadCellIndices.Add(cellIndex);
            quadShades.Add(shade);
        }

        private void RefreshColours(
            NavGridSettings gridSettings,
            NavGridDebugSettings debugSettings,
            NativeArray<byte> costs,
            double now)
        {
            if (quadCellIndices.Count == 0 || debugMesh == null) return;
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
            int quadCount = quadCellIndices.Count;
            int fillVertexCount = quadCount * VerticesPerQuad;

            meshColors.Clear();
            if (meshColors.Capacity < meshVertices.Count) meshColors.Capacity = meshVertices.Count;
            for (int vertex = 0; vertex < meshVertices.Count; vertex++) meshColors.Add(default);

            for (int quad = 0; quad < quadCount; quad++)
            {
                int cellIndex = quadCellIndices[quad];
                float shade = quadShades[quad];
                float4 cellColour = ResolveCellColour(costs[cellIndex], cellIndex, gridSettings, debugSettings, now);

                Color fillColour = new Color(cellColour.x * shade, cellColour.y * shade, cellColour.z * shade, cellColour.w);
                Color outlineColour = new Color(
                    math.min(1f, cellColour.x * OutlineBrightnessBoost),
                    math.min(1f, cellColour.y * OutlineBrightnessBoost),
                    math.min(1f, cellColour.z * OutlineBrightnessBoost),
                    math.min(1f, cellColour.w * OutlineAlphaBoost));

                int fillVertex = quad * VerticesPerQuad;
                for (int corner = 0; corner < VerticesPerQuad; corner++)
                {
                    meshColors[fillVertex + corner] = fillColour;
                    if (outlinesInCurrentMesh)
                        meshColors[fillVertexCount + fillVertex + corner] = outlineColour;
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
                    $"NavGrid debug: FullGrid would draw {candidateCells} cells, over the maxDrawnCells " +
                    $"budget of {debugSettings.maxDrawnCells}. Falling back to ObstaclesOnly. Raise the budget " +
                    "on NavGridAuthoring, or pick a single layer, if you need the whole grid.");
            }
            return NavGridDebugDisplayMode.ObstaclesOnly;
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
            meshVertices.Clear();
            meshColors.Clear();
            fillIndices.Clear();
            outlineIndices.Clear();
            quadCellIndices.Clear();
            quadShades.Clear();
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
                && left.obstacleExtrusionHeight.Equals(right.obstacleExtrusionHeight)
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
                && left.layerHeight.Equals(right.layerHeight)
                && left.gridOrigin.Equals(right.gridOrigin);
        }
    }
}
#endif
