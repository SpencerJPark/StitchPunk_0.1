#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for DialogueSequenceSO — adds an "Open in Dialogue Editor" button
/// above the default inspector fields.
/// </summary>
[CustomEditor(typeof(DialogueSequenceSO))]
public class DialogueSequenceSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Open in Dialogue Editor", GUILayout.Height(30f)))
        {
           DialogueSequenceEditorWindow.OpenWith((DialogueSequenceSO)target);
        }

        EditorGUILayout.Space(6f);
        DrawDefaultInspector();
    }
}
#endif
