using DotsMovementToolkit;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsMovementToolkit.Authoring
{
    // Bakes the NavGridSettings singleton the whole toolkit gates on, plus the optional
    // NavGridDebugSettings that drives NavGridDebugRenderSystem. One per project — add to a
    // subscene alongside the rest of the game's baked config.
    [AddComponentMenu("DOTS Movement Toolkit/Nav Grid")]
    public class NavGridAuthoring : MonoBehaviour
    {
        [Header("Grid")]
        public int width = 100;
        public int height = 100;
        public int layerCount = 1;
        public float cellSize = 2f;
        public float layerHeight = 3f;

        [Tooltip("Centre the footprint on this GameObject. Off anchors cell (0,0) at it instead, so the grid only extends into +X/+Z.")]
        public bool centerGridOnThisTransform = true;

        [Header("Physics Layers")]
        public LayerMask wallLayerMask;
        public LayerMask heavyLayerMask;
        public LayerMask groundLayerMask;

        [Header("Costs")]
        public byte wallCost = byte.MaxValue;
        public byte heavyCost = 50;
        public byte defaultCost = 1;

        [Header("Debug View")]
        [Tooltip("Draws the live cost map in Play mode (Game and Scene view). Editor and development builds only.")]
        public NavGridDebugDisplayMode debugDisplayMode = NavGridDebugDisplayMode.Off;

        [Tooltip("Which layer to draw. -1 draws every layer at its own height.")]
        public int debugLayerToDraw = 0;

        [Tooltip("Lift above the layer floor, to stop the tiles z-fighting with the ground.")]
        public float debugHeightOffset = 0.05f;

        [Range(0f, 0.45f)]
        [Tooltip("Gap shrunk from every cell edge, so individual tiles stay readable.")]
        public float debugCellPadding = 0.08f;

        public bool debugDrawCellOutlines = true;

        [Tooltip("Height blocked cells are extruded to, so obstacles read as solid blocks rather than paint on the floor. Discouraged cells get half. 0 keeps everything flat.")]
        public float debugObstacleExtrusionHeight = 2f;

        [Tooltip("How long a cell stays flashed after its cost changes — this is what makes live obstacles visible. 0 disables change tracking.")]
        public float debugChangeHighlightSeconds = 1.5f;

        [Tooltip("Ceiling on drawn cells. A Full Grid over this budget falls back to Obstacles Only instead of stalling the editor.")]
        public int debugMaxDrawnCells = 40000;

        public Color debugWalkableColor = new Color(0.25f, 0.85f, 0.45f, 0.14f);
        public Color debugDiscouragedColor = new Color(1f, 0.62f, 0.05f, 0.70f);
        public Color debugBlockedColor = new Color(1f, 0.15f, 0.18f, 0.85f);
        public Color debugRecentlyChangedColor = new Color(0.30f, 0.90f, 1f, 0.95f);

        [Header("Editor Gizmo")]
        [Tooltip("Draws the grid footprint in the Scene view outside Play mode, where no cost map exists yet.")]
        public bool drawBoundsGizmo = true;

        [Tooltip("Also draw the cell lattice when this object is selected. Off for large grids.")]
        public bool drawLatticeGizmoWhenSelected = true;

        /// <summary>
        /// World position of cell (0,0)'s corner. The grid always extends into +X/+Z from here, so
        /// centring on the transform means the origin sits half a footprint back in X and Z.
        /// </summary>
        public Vector3 ResolveGridOrigin()
        {
            Vector3 origin = transform.position;
            if (centerGridOnThisTransform)
            {
                origin.x -= width * cellSize * 0.5f;
                origin.z -= height * cellSize * 0.5f;
            }
            return origin;
        }

        public class Baker : Baker<NavGridAuthoring>
        {
            public override void Bake(NavGridAuthoring authoring)
            {
                // The grid origin is derived from the transform, so the transform is a real baking
                // input — without this, moving the GameObject would not re-bake and the grid would
                // silently stay where it was.
                DependsOn(authoring.transform);

                Vector3 gridOrigin = authoring.ResolveGridOrigin();
                Entity entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new NavGridSettings
                {
                    width = authoring.width,
                    height = authoring.height,
                    layerCount = authoring.layerCount,
                    cellSize = authoring.cellSize,
                    layerHeight = authoring.layerHeight,
                    gridOrigin = new float3(gridOrigin.x, gridOrigin.y, gridOrigin.z),
                    wallLayerMask = (uint)(int)authoring.wallLayerMask,
                    heavyLayerMask = (uint)(int)authoring.heavyLayerMask,
                    groundLayerMask = (uint)(int)authoring.groundLayerMask,
                    wallCost = authoring.wallCost,
                    heavyCost = authoring.heavyCost,
                    defaultCost = authoring.defaultCost,
                });

                AddComponent(entity, new NavGridDebugSettings
                {
                    displayMode = authoring.debugDisplayMode,
                    layerToDraw = authoring.debugLayerToDraw,
                    heightOffset = authoring.debugHeightOffset,
                    cellPadding = authoring.debugCellPadding,
                    drawCellOutlines = authoring.debugDrawCellOutlines,
                    obstacleExtrusionHeight = authoring.debugObstacleExtrusionHeight,
                    changeHighlightSeconds = authoring.debugChangeHighlightSeconds,
                    maxDrawnCells = authoring.debugMaxDrawnCells,
                    walkableColor = ToFloat4(authoring.debugWalkableColor),
                    discouragedColor = ToFloat4(authoring.debugDiscouragedColor),
                    blockedColor = ToFloat4(authoring.debugBlockedColor),
                    recentlyChangedColor = ToFloat4(authoring.debugRecentlyChangedColor),
                });
            }

            private static float4 ToFloat4(Color color) => new float4(color.r, color.g, color.b, color.a);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawBoundsGizmo) return;
            DrawFootprintGizmo(new Color(0.35f, 0.75f, 1f, 0.35f));
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawBoundsGizmo) return;
            DrawFootprintGizmo(new Color(0.35f, 0.75f, 1f, 0.9f));
            if (drawLatticeGizmoWhenSelected) DrawLatticeGizmo();
        }

        private void DrawFootprintGizmo(Color color)
        {
            Gizmos.color = color;
            Vector3 origin = ResolveGridOrigin();
            float extentX = width * cellSize;
            float extentZ = height * cellSize;

            for (int layer = 0; layer < Mathf.Max(1, layerCount); layer++)
            {
                float y = origin.y + layer * layerHeight + debugHeightOffset;
                Vector3 corner0 = new Vector3(origin.x, y, origin.z);
                Vector3 corner1 = new Vector3(origin.x + extentX, y, origin.z);
                Vector3 corner2 = new Vector3(origin.x + extentX, y, origin.z + extentZ);
                Vector3 corner3 = new Vector3(origin.x, y, origin.z + extentZ);

                Gizmos.DrawLine(corner0, corner1);
                Gizmos.DrawLine(corner1, corner2);
                Gizmos.DrawLine(corner2, corner3);
                Gizmos.DrawLine(corner3, corner0);
            }
        }

        private void DrawLatticeGizmo()
        {
            Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.18f);
            Vector3 origin = ResolveGridOrigin();
            float extentX = width * cellSize;
            float extentZ = height * cellSize;
            float y = origin.y + Mathf.Max(0, debugLayerToDraw) * layerHeight + debugHeightOffset;

            for (int column = 0; column <= width; column++)
            {
                float x = origin.x + column * cellSize;
                Gizmos.DrawLine(new Vector3(x, y, origin.z), new Vector3(x, y, origin.z + extentZ));
            }

            for (int row = 0; row <= height; row++)
            {
                float z = origin.z + row * cellSize;
                Gizmos.DrawLine(new Vector3(origin.x, y, z), new Vector3(origin.x + extentX, y, z));
            }
        }
#endif
    }
}
