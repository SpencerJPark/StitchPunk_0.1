using UnityEngine;

namespace PaintedGround
{
    /// One projected decal. The transform is the volume: X/Z footprint, Y depth reach.
    /// Sets the atlas tile per instance through a MaterialPropertyBlock so hundreds of
    /// decals can share two materials (one Multiply, one Normal).
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
    public class PaintedDecal : MonoBehaviour
    {
        public int tile;
        [Range(0f, 1f)] public float opacity = 1f;
        public Color tint = Color.white;

        static readonly int TileId = Shader.PropertyToID("_Tile");
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static MaterialPropertyBlock _mpb;

        void OnEnable()  { Apply(); }
        void OnValidate() { Apply(); }

        public void Apply()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;
            _mpb ??= new MaterialPropertyBlock();
            mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(TileId, tile);
            var c = tint; c.a *= opacity;
            _mpb.SetColor(TintId, c);
            mr.SetPropertyBlock(_mpb);
        }

        /// Create a decal at a world point on the ground.
        public static PaintedDecal Create(Material mat, Mesh cube, Vector3 pos, float yaw, float size, float depth, int tile, Transform parent, string name = "Decal")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(size, depth, size);
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = cube;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            var d = go.AddComponent<PaintedDecal>();
            d.tile = tile;
            d.Apply();
            return d;
        }
    }
}
