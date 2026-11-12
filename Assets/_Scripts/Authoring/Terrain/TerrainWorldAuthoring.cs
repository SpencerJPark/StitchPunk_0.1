using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[System.Serializable]
public struct TerrainTileSceneRef
{
    public Vector2Int coordinate;
    // AssetDatabase GUID string (32 hex chars) of the tile's .unity scene.
    public string sceneGuid;
}

// Lives inside a subscene, which cannot read other scenes while baking, so the tile list is serialized and filled by an editor tool.
[AddComponentMenu("Stitch Punk/Terrain/Terrain World")]
public class TerrainWorldAuthoring : MonoBehaviour
{
    public int loadRadiusTiles = 1;
    public int unloadRadiusTiles = 2;
    public int maxLoadsPerFrame = 1;
    public List<TerrainTileSceneRef> tiles = new List<TerrainTileSceneRef>();

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<TerrainWorldAuthoring>
    {
        public override void Bake(TerrainWorldAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            int loadRadius = math.max(0, authoring.loadRadiusTiles);
            // Unload radius must exceed load radius or units on a tile border thrash load/unload.
            int unloadRadius = math.max(loadRadius + 1, authoring.unloadRadiusTiles);
            int loadsPerFrame = math.max(1, authoring.maxLoadsPerFrame);
            if (loadRadius != authoring.loadRadiusTiles || unloadRadius != authoring.unloadRadiusTiles || loadsPerFrame != authoring.maxLoadsPerFrame)
            {
                Debug.LogWarning("TerrainWorldAuthoring '" + authoring.name + "': streaming settings were clamped (load >= 0, unload >= load + 1, loads per frame >= 1).", authoring);
            }

            AddComponent(entity, new TerrainStreamingSettings
            {
                loadRadiusTiles = loadRadius,
                unloadRadiusTiles = unloadRadius,
                maxLoadsPerFrame = loadsPerFrame
            });

            DynamicBuffer<TerrainTileEntry> buffer = AddBuffer<TerrainTileEntry>(entity);
            HashSet<Vector2Int> seenCoordinates = new HashSet<Vector2Int>();
            foreach (TerrainTileSceneRef tile in authoring.tiles)
            {
                Unity.Entities.Hash128 sceneGuid = new Unity.Entities.Hash128(tile.sceneGuid ?? string.Empty);
                if (!sceneGuid.IsValid)
                {
                    Debug.LogWarning("TerrainWorldAuthoring: tile " + tile.coordinate + " has an invalid scene guid and was skipped.", authoring);
                    continue;
                }
                if (!seenCoordinates.Add(tile.coordinate))
                {
                    Debug.LogWarning("TerrainWorldAuthoring: duplicate tile coordinate " + tile.coordinate + " skipped (first entry wins).", authoring);
                    continue;
                }

                buffer.Add(new TerrainTileEntry
                {
                    coordinate = new int2(tile.coordinate.x, tile.coordinate.y),
                    sceneGuid = sceneGuid,
                    state = TerrainTileState.Unloaded,
                    sceneEntity = Entity.Null
                });
            }
        }
    }
}
