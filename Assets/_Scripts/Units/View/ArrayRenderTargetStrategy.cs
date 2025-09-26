// using System;
// using System.Collections.Generic;
// using UnityEngine;
// using Rive.Components;

// public class TextureArrayRenderTargetStrategy : IRenderTargetStrategy
// {
//     // Default slice size
//     private const int DefaultSliceResolution = 128;
//     private const int InitialSliceCount = 16;

//     public DrawTimingOption DrawTiming { get; set; } = DrawTimingOption.DrawBatched;

//     private List<IRivePanel> panels = new List<IRivePanel>();
//     private Dictionary<IRivePanel, int> panelSliceIndex = new Dictionary<IRivePanel, int>();

//     private Texture2DArray textureArray;
//     private int sliceResolution = DefaultSliceResolution;
//     private int sliceCount = InitialSliceCount;

//     public event Action<IRivePanel> OnRenderTargetUpdated;
//     public event Action<IRivePanel> OnPanelRegistered;
//     public event Action<IRivePanel> OnPanelUnregistered;

//     public TextureArrayRenderTargetStrategy()
//     {
//         CreateTextureArray(sliceCount);
//     }

//     private void CreateTextureArray(int count)
//     {
//         textureArray = new Texture2DArray(sliceResolution, sliceResolution, count, TextureFormat.RGBA32, false)
//         {
//             filterMode = FilterMode.Bilinear,
//             wrapMode = TextureWrapMode.Clamp
//         };
//     }

//     private void ExpandTextureArray(int newSize)
//     {
//         var newArray = new Texture2DArray(sliceResolution, sliceResolution, newSize, TextureFormat.RGBA32, false)
//         {
//             filterMode = FilterMode.Bilinear,
//             wrapMode = TextureWrapMode.Clamp
//         };

//         for (int i = 0; i < panels.Count; i++)
//         {
//             Graphics.CopyTexture(textureArray, i, 0, newArray, i, 0);
//         }

//         textureArray = newArray;
//         sliceCount = newSize;
//     }

//     public bool RegisterPanel(IRivePanel panel)
//     {
//         if (panels.Contains(panel)) return false;

//         if (panels.Count >= sliceCount)
//             ExpandTextureArray(sliceCount * 2);

//         panels.Add(panel);
//         panelSliceIndex[panel] = panels.Count - 1;

//         OnPanelRegistered?.Invoke(panel);
//         return true;
//     }

//     public bool UnregisterPanel(IRivePanel panel)
//     {
//         if (!panels.Contains(panel)) return false;

//         int removedIndex = panelSliceIndex[panel];
//         panels.Remove(panel);
//         panelSliceIndex.Remove(panel);

//         // shift indices of subsequent panels
//         for (int i = removedIndex; i < panels.Count; i++)
//             panelSliceIndex[panels[i]] = i;

//         OnPanelUnregistered?.Invoke(panel);
//         return true;
//     }

//     public bool IsPanelRegistered(IRivePanel panel) => panels.Contains(panel);

//     public int GetPanelSlice(IRivePanel panel) => panelSliceIndex.TryGetValue(panel, out var index) ? index : -1;

//     public RenderTexture GetRenderTexture(IRivePanel panel) => textureArray as RenderTexture;

//     // In texture array, each panel occupies a full slice
//     public Vector2 GetPanelScale(IRivePanel panel) => Vector2.one;
//     public Vector2 GetPanelOffset(IRivePanel panel) => Vector2.zero;

//     public void DrawPanel(IRivePanel panel)
//     {
//         int slice = GetPanelSlice(panel);
//         if (slice < 0) return;

//         // TODO: implement Rive panel rendering into slice
//         // Example placeholder:
//         // Graphics.Blit(panel.GetRenderTexture(), textureArray, slice);

//         OnRenderTargetUpdated?.Invoke(panel);
//     }
// }
