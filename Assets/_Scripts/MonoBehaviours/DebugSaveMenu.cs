using UnityEngine;

/// <summary>
/// Throwaway OnGUI test menu for the save system — Save / Load buttons in the top-left corner.
/// Drop this on any GameObject in the scene; it auto-attaches a <see cref="SaveLoadBridge"/> and
/// flips the ECS SaveRequest / LoadRequest. Delete once the real menu UI exists.
/// </summary>
public class DebugSaveMenu : MonoBehaviour
{
    [Tooltip("Save slot to use. 0 = autosave, 1–3 = manual.")]
    [SerializeField] private int slot = 1;

    private SaveLoadBridge bridge;

    private void Awake()
    {
        bridge = GetComponent<SaveLoadBridge>();
        if (bridge == null)
            bridge = gameObject.AddComponent<SaveLoadBridge>();
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10f, 10f, 160f, 200f), GUI.skin.box);
        GUILayout.Label($"Save System (slot {slot})");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-")) slot = Mathf.Max(0, slot - 1);
        if (GUILayout.Button("+")) slot = Mathf.Min(3, slot + 1);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Save", GUILayout.Height(40f)))
            bridge.RequestSave(slot);

        if (GUILayout.Button("Load", GUILayout.Height(40f)))
            bridge.RequestLoad(slot);

        GUILayout.EndArea();
    }
}
