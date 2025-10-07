using System.Collections.Generic;
using UnityEngine;
using Rive.Components; // for RivePanel

/// <summary>
/// Shares a single RenderTexture (e.g., from a RivePanel) across many renderers.
/// Hands out "channels" = tiles in a fixed grid (default 8x8 for 4096/512).
/// Applies per-renderer tiling/offset via MaterialPropertyBlock (no material instances).
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class RiveRadio : MonoBehaviour
{
    public static RiveRadio Instance { get; private set; }

    [Header("Source")]
    [Tooltip("If set, will pull RenderTexture from this RivePanel each frame.")]
    [SerializeField] private RivePanel rivePanel;

    [Tooltip("Override: if set, use this texture instead of pulling from RivePanel.")]
    [SerializeField] private RenderTexture overrideTexture;

    [Header("Grid")]
    [Tooltip("Pixels per tile (e.g., 512 for a 4096 texture => 8x8).")]
    [SerializeField] private int tileSizePixels = 512;

    [Tooltip("Treat tile row 0 at top (UV V inverted).")]
    [SerializeField] private bool topLeftOrigin = true;

    [Header("Shader Property Names (auto if empty)")]
    [SerializeField] private string texturePropertyName = ""; // auto-detect _BaseMap/_MainTex
    [SerializeField] private string stPropertyName      = ""; // auto-detect *_ST

    // Runtime
    private Texture _sharedTex;
    private int _atlasWidth  = 4096;
    private int _atlasHeight = 4096;

    private int _tilesX = 8;
    private int _tilesY = 8;
    private int _maxChannels = 64;

    private readonly List<RiveRadioClient> _clients = new();
    private bool[] _occupied; // size = _maxChannels

    // Cached shader property IDs
    private int _texId;
    private int _stId;
    private bool _idsResolved;

    void Awake()
    {
        // if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        // Instance = this;
        RecomputeGrid();
        _occupied = new bool[_maxChannels];
    }

    // void OnDestroy()
    // {
    //     if (Instance == this) Instance = null;
    //     _clients.Clear();
    //     _occupied = null;
    // }

    void Update()
    {
        // Decide which texture to use
        Texture tex = overrideTexture ? overrideTexture : (rivePanel ? rivePanel.RenderTexture : null);

        // Track size changes (recompute grid)
        if (tex != null)
        {
            if (_sharedTex != tex || _atlasWidth != tex.width || _atlasHeight != tex.height)
            {
                _sharedTex = tex;
                _atlasWidth = tex.width;
                _atlasHeight = tex.height;
                RecomputeGrid();
                // Re-apply to all clients so they get the new ST/texture
                ReapplyAllClients();
            }
        }
    }

    private void RecomputeGrid()
    {
        if (tileSizePixels <= 0) tileSizePixels = 512;
        _tilesX = Mathf.Max(1, _atlasWidth  / tileSizePixels);
        _tilesY = Mathf.Max(1, _atlasHeight / tileSizePixels);
        _maxChannels = _tilesX * _tilesY;

        // Resize occupancy if needed
        if (_occupied == null || _occupied.Length != _maxChannels)
            _occupied = new bool[_maxChannels];

        // Clamp any client indices that overflow
        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            var c = _clients[i];
            if (c == null) { _clients.RemoveAt(i); continue; }
            if (c.ChannelIndex >= _maxChannels) c.ChannelIndex = -1; // force re-alloc
        }
    }

    public Texture SharedTexture => _sharedTex;

    // -------- Registration & allocation --------

    public void Register(RiveRadioClient client)
    {
        if (client == null || _clients.Contains(client)) return;
        _clients.Add(client);

        if (client.RequestSpecific && client.ChannelIndex >= 0)
        {
            // Try to honor requested index; if occupied, find a free one
            if (client.ChannelIndex >= _maxChannels || _occupied[client.ChannelIndex])
                client.ChannelIndex = FindFreeChannel();
        }
        else
        {
            client.ChannelIndex = FindFreeChannel();
        }

        if (client.ChannelIndex >= 0) _occupied[client.ChannelIndex] = true;

        ApplyToClient(client);
    }

    public void Unregister(RiveRadioClient client)
    {
        if (client == null) return;
        if (_clients.Remove(client) && client.ChannelIndex >= 0 && client.ChannelIndex < _maxChannels)
            _occupied[client.ChannelIndex] = false;
    }

    public void UpdateClientST(RiveRadioClient client)
    {
        ApplyToClient(client);
    }

    private int FindFreeChannel()
    {
        for (int i = 0; i < _maxChannels; i++)
            if (!_occupied[i]) return i;
        return -1;
    }

    private void ReapplyAllClients()
    {
        for (int i = 0; i < _clients.Count; i++)
            ApplyToClient(_clients[i]);
    }

    // -------- Per-renderer application (MPB) --------

    private void EnsurePropertyIds(Renderer r)
    {
        if (_idsResolved) return;

        // Auto-detect property names if empty
        var mat = r ? r.sharedMaterial : null;
        bool isURP = mat && mat.HasProperty("_BaseMap");
        if (string.IsNullOrEmpty(texturePropertyName))
            texturePropertyName = isURP ? "_BaseMap" : "_MainTex";
        if (string.IsNullOrEmpty(stPropertyName))
            stPropertyName = isURP ? "_BaseMap_ST" : "_MainTex_ST";

        _texId = Shader.PropertyToID(texturePropertyName);
        _stId  = Shader.PropertyToID(stPropertyName);
        _idsResolved = true;
    }

    private void ApplyToClient(RiveRadioClient client)
    {
        if (client == null || client.Renderer == null) return;

        EnsurePropertyIds(client.Renderer);

        var mpb = client.SharedMPB ?? (client.SharedMPB = new MaterialPropertyBlock());
        client.Renderer.GetPropertyBlock(mpb);

        // Texture
        if (_sharedTex != null) mpb.SetTexture(_texId, _sharedTex);

        // ST (scale/offset) from channel
        if (client.ChannelIndex >= 0 && _maxChannels > 0)
        {
            var st = ComputeST(client.ChannelIndex);
            mpb.SetVector(_stId, st);
        }

        // Optional per-instance tint or extras
        if (client.useTint) mpb.SetColor(client.TintPropertyId, client.tint);

        client.Renderer.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// Returns (scaleX, scaleY, offsetX, offsetY) for a given channel index.
    /// Indexing is left-to-right, top-to-bottom if topLeftOrigin is true.
    /// </summary>
    private Vector4 ComputeST(int channelIndex)
    {
        if (_tilesX < 1) _tilesX = 1;
        if (_tilesY < 1) _tilesY = 1;

        int idx = Mathf.Clamp(channelIndex, 0, _maxChannels - 1);
        int x = idx % _tilesX;
        int y = idx / _tilesX;

        float sx = (float)tileSizePixels / Mathf.Max(1, _atlasWidth);
        float sy = (float)tileSizePixels / Mathf.Max(1, _atlasHeight);

        float u = x * sx;
        float v = topLeftOrigin ? (1f - sy - y * sy) : (y * sy);

        return new Vector4(sx, sy, u, v);
    }
}
