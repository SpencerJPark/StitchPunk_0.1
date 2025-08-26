// using System.Collections.Generic;
// using UnityEngine;
// using UnityEngine.Rendering;

// namespace Rive.Components
// {
//     [AddComponentMenu("Rive/Render Target Strategies/Array Render Target Strategy")]
//     public class ArrayRenderTargetStrategy : RenderTargetStrategy
//     {
//         [Header("Array Settings")]
//         [SerializeField] private Vector2Int panelSize = new Vector2Int(128, 128);
//         [SerializeField] private int initialSlices = 64;
//         [SerializeField] private int maxSlices = 512;
//         [SerializeField] private RenderTextureFormat format = RenderTextureFormat.ARGB32;
//         [SerializeField] private bool useMipMaps = false;

//         [Header("Timing")]
//         [SerializeField] private DrawTimingOption drawTiming = DrawTimingOption.DrawBatched;
//         public override DrawTimingOption DrawTiming { get => drawTiming; set => drawTiming = value; }

//         // Shared array that holds all panel outputs.
//         private RenderTexture arrayRT;

//         // Rive renderer (same pattern used by the atlas strategy)
//         private Renderer riveRenderer;
//         private bool rendererRegistered = false;

//         // Each panel gets a slice; we keep an index map.
//         private readonly Dictionary<IRivePanel, int> panelToSlice = new();
//         private readonly List<IRivePanel> panels = new();

//         // Temp RT pool (so we don’t allocate every draw)
//         private readonly Stack<RenderTexture> tempPool = new();

//         // Batched redraw flag
//         private bool needsRedraw;

//         // ----------------- Public API -----------------

//         public override bool RegisterPanel(IRivePanel panel)
//         {
//             if (panel == null || IsDestroyed) return false;
//             if (panelToSlice.ContainsKey(panel)) return false;

//             EnsureArrayCapacity(panels.Count + 1);

//             panels.Add(panel);
//             panelToSlice[panel] = panels.Count - 1; // simple 0..N-1 assignment

//             EnsureRenderer();

//             if (!rendererRegistered)
//             {
//                 RegisterRenderer(riveRenderer);
//                 rendererRegistered = true;
//             }

//             TriggerPanelRegisteredEvent(panel);
//             QueueRedraw();
//             return true;
//         }

//         public override bool UnregisterPanel(IRivePanel panel)
//         {
//             if (!panelToSlice.TryGetValue(panel, out var slice)) return false;

//             // Keep it simple: just remove from map/list; we won’t compact slices for stability.
//             panelToSlice.Remove(panel);
//             panels.Remove(panel);

//             TriggerPanelUnregisteredEvent(panel);
//             QueueRedraw();

//             if (panels.Count == 0 && rendererRegistered)
//             {
//                 UnregisterRenderer(riveRenderer);
//                 rendererRegistered = false;
//             }
//             return true;
//         }

//         public override bool IsPanelRegistered(IRivePanel panel) => panelToSlice.ContainsKey(panel);

//         public override void DrawPanel(IRivePanel panel)
//         {
//             if (!panelToSlice.TryGetValue(panel, out var slice)) return;

//             EnsureRenderer();
//             EnsureArrayCapacity(slice + 1);

//             // 1) Acquire a temporary 2D RT to let Rive render this panel
//             var temp = AcquireTempRT();
//             RenderPipelineHandler.SetRendererTexture(riveRenderer, temp);

//             // 2) Draw the panel to temp (full coverage)
//             var targetInfo = new RenderTargetInfo(
//                 renderTargetSize: panelSize,
//                 panelAllocation:  panelSize
//             );
//             riveRenderer.Clear();
//             DrawPanelWithRenderer(riveRenderer, panel, targetInfo, RenderTargetSpaceOccupancy.Exclusive);

//             // 3) GPU-copy temp → array slice
//             // Copy the full mip 0 to the destination slice
//             Graphics.CopyTexture(temp, 0, 0, arrayRT, slice, 0);

//             // Optionally generate mips on the array if you need them
//             if (useMipMaps) arrayRT.GenerateMips();

//             ReleaseTempRT(temp);
//             TriggerRenderTargetUpdatedEvent(panel);
//         }

//         public override Vector2 GetPanelOffset(IRivePanel panel) => Vector2.zero;
//         public override Vector2 GetPanelScale(IRivePanel panel)  => Vector2.one;
//         public override RenderTexture GetRenderTexture(IRivePanel panel) => arrayRT;

//         // ----------------- Mono Loop -----------------

//         private void LateUpdate()
//         {
//             if (drawTiming != DrawTimingOption.DrawBatched || !needsRedraw) return;
//             needsRedraw = false;

//             // Redraw all registered panels into their slices
//             foreach (var p in panels)
//             {
//                 if (p != null) DrawPanel(p);
//             }
//         }

//         protected override IEnumerable<Renderer> GetRenderers()
//         {
//             if (riveRenderer != null) yield return riveRenderer;
//         }

//         protected override void OnDestroy()
//         {
//             base.OnDestroy();

//             // Cleanup array
//             if (arrayRT != null)
//             {
//                 ReleaseRenderTexture(arrayRT);
//                 Destroy(arrayRT);
//                 arrayRT = null;
//             }

//             // Cleanup renderer
//             if (riveRenderer != null)
//             {
//                 RendererUtils.ReleaseRenderer(riveRenderer);
//                 riveRenderer = null;
//             }

//             // Cleanup temps
//             while (tempPool.Count > 0)
//             {
//                 var rt = tempPool.Pop();
//                 ReleaseRenderTexture(rt);
//                 Destroy(rt);
//             }
//         }

//         // ----------------- Internals -----------------

//         private void EnsureRenderer()
//         {
//             if (riveRenderer != null) return;
//             riveRenderer = RendererUtils.CreateRenderer();
//             // We set the texture per-draw (temp RT), so no persistent binding here.
//         }

//         private void EnsureArrayCapacity(int requiredSlices)
//         {
//             int targetSlices = Mathf.Clamp(Mathf.Max(requiredSlices, initialSlices), 1, maxSlices);

//             // If current array is OK, keep it
//             if (arrayRT != null &&
//                 arrayRT.width == panelSize.x &&
//                 arrayRT.height == panelSize.y &&
//                 arrayRT.volumeDepth >= targetSlices)
//             {
//                 return;
//             }

//             // Make / Resize the array
//             var newRt = new RenderTexture(panelSize.x, panelSize.y, 0, format)
//             {
//                 dimension = TextureDimension.Tex2DArray,
//                 volumeDepth = targetSlices,
//                 useMipMap = useMipMaps,
//                 autoGenerateMips = useMipMaps,
//             };
//             newRt.Create();

//             // If we had an old array, we could copy existing slices (optional)
//             if (arrayRT != null)
//             {
//                 ReleaseRenderTexture(arrayRT);
//                 Destroy(arrayRT);
//             }

//             arrayRT = newRt;
//         }

//         private RenderTexture AcquireTempRT()
//         {
//             if (tempPool.Count > 0)
//             {
//                 var pooled = tempPool.Pop();
//                 if (pooled != null && pooled.IsCreated() && pooled.width == panelSize.x && pooled.height == panelSize.y)
//                     return pooled;
//                 // else discard and make a fresh one
//             }

//             var rt = CreateRenderTexture(panelSize.x, panelSize.y);
//             if (!rt.IsCreated()) rt.Create();
//             return rt;
//         }

//         private void ReleaseTempRT(RenderTexture rt)
//         {
//             if (rt == null) return;
//             tempPool.Push(rt);
//         }

//         private void QueueRedraw()
//         {
//             if (drawTiming == DrawTimingOption.DrawImmediate)
//             {
//                 // Draw immediately
//                 foreach (var p in panels)
//                     if (p != null) DrawPanel(p);
//             }
//             else
//             {
//                 needsRedraw = true;
//             }
//         }
//     }
// }
