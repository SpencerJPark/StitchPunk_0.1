#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Dev tool for the style guide's R22 (captures before/after every visual change): photographs each Terrain Editor tab
// through the window's GUIView, the recipe in Memories/Code/AnimationToolkit.md. Repaints ~35 times per tab first, so it also works
// while the Editor is unfocused.
public static class TerrainEditorCapture
{
    const BindingFlags AnyInstance = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const int RepaintsBeforeGrab = 35;
    const string DefaultFolder = "Library/TerrainEditorCaptures";

    static readonly string[] PanelNamesByIndex = { "world", "paint", "sculpt", "generate", "roads", "water", "decals", "props", "nav", "features" };

    public static bool IsRunning { get; private set; }

    [MenuItem("Stitch Punk/Terrain/Capture Editor Tabs")]
    static void CaptureFromMenu()
    {
        CaptureAllTabs(DefaultFolder, new Vector2(760f, 900f));
    }

    public static void CaptureAllTabs(string folder, Vector2 windowSize)
    {
        CaptureTabs(folder, windowSize, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
    }

    public static void CaptureTabs(string folder, Vector2 windowSize, int[] panelIndices)
    {
        if (IsRunning) return;
        Directory.CreateDirectory(folder);

        TerrainEditorWindow.Open();
        TerrainEditorWindow window = EditorWindow.GetWindow<TerrainEditorWindow>();
        window.position = new Rect(100f, 80f, windowSize.x, windowSize.y);
        object hostView = typeof(EditorWindow).GetField("m_Parent", AnyInstance).GetValue(window);
        MethodInfo selectTool = typeof(TerrainEditorWindow).GetMethod("SelectTool", AnyInstance);
        MethodInfo repaint = hostView.GetType().GetMethod("RepaintImmediately", AnyInstance);
        MethodInfo grabPixels = typeof(EditorWindow).Assembly.GetType("UnityEditor.GUIView").GetMethod("GrabPixels", AnyInstance);
        PropertyInfo screenPosition = hostView.GetType().GetProperty("screenPosition", AnyInstance);

        int listIndex = 0;
        int frame = 0;
        IsRunning = true;
        EditorApplication.CallbackFunction step = null;
        step = () =>
        {
            if (window == null || listIndex >= panelIndices.Length)
            {
                EditorApplication.update -= step;
                IsRunning = false;
                Debug.Log("[TerrainCapture] done: " + folder);
                return;
            }
            int panelIndex = panelIndices[listIndex];
            if (frame == 0) selectTool.Invoke(window, new object[] { panelIndex });
            frame++;
            if (repaint != null) repaint.Invoke(hostView, null);
            if (frame < RepaintsBeforeGrab) return;

            SaveGrab(hostView, grabPixels, screenPosition, Path.Combine(folder, PanelNamesByIndex[panelIndex] + ".png"));
            listIndex++;
            frame = 0;
        };
        EditorApplication.update += step;
    }

    static void SaveGrab(object hostView, MethodInfo grabPixels, PropertyInfo screenPosition, string path)
    {
        float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
        Rect hostRect = (Rect)screenPosition.GetValue(hostView, null);
        int widthPixels = Mathf.RoundToInt(hostRect.width * pixelsPerPoint);
        int heightPixels = Mathf.RoundToInt(hostRect.height * pixelsPerPoint);
        RenderTexture renderTexture = new RenderTexture(widthPixels, heightPixels, 0, RenderTextureFormat.ARGB32);
        grabPixels.Invoke(hostView, new object[] { renderTexture, new Rect(0, 0, widthPixels, heightPixels) });

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(widthPixels, heightPixels, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, widthPixels, heightPixels), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;

        // The grab is bottom-up; flip it before saving.
        Color32[] pixels = texture.GetPixels32();
        Color32[] flipped = new Color32[pixels.Length];
        for (int row = 0; row < heightPixels; row++)
            Array.Copy(pixels, row * widthPixels, flipped, (heightPixels - 1 - row) * widthPixels, widthPixels);
        texture.SetPixels32(flipped);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());

        UnityEngine.Object.DestroyImmediate(texture);
        renderTexture.Release();
        UnityEngine.Object.DestroyImmediate(renderTexture);
    }
}
#endif
