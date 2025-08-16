// Editor/RuntimeWatchInspector.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;

[CanEditMultipleObjects]
[CustomEditor(typeof(MonoBehaviour), true)] // applies to all MonoBehaviours
public class RuntimeWatchInspector : Editor
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public override void OnInspectorGUI()
    {
        // draw the normal inspector first
        DrawDefaultInspector();

        var t = target.GetType();

        // find watched members
        var watchedFields = t.GetFields(Flags)
            .Where(f => f.GetCustomAttribute<RuntimeWatchAttribute>(true) != null);
        var watchedProps = t.GetProperties(Flags)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 &&
                        p.GetCustomAttribute<RuntimeWatchAttribute>(true) != null);

        if (!watchedFields.Any() && !watchedProps.Any())
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Watch", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            foreach (var f in watchedFields)
            {
                var attr = f.GetCustomAttribute<RuntimeWatchAttribute>(true);
                string label = string.IsNullOrEmpty(attr.Label) ? ObjectNames.NicifyVariableName(f.Name) : attr.Label;
                DrawValue(label, SafeGet(() => f.GetValue(target)));
            }

            foreach (var p in watchedProps)
            {
                var attr = p.GetCustomAttribute<RuntimeWatchAttribute>(true);
                string label = string.IsNullOrEmpty(attr.Label) ? ObjectNames.NicifyVariableName(p.Name) : attr.Label;
                DrawValue(label, SafeGet(() => p.GetValue(target, null)));
            }
        }

        if (Application.isPlaying) Repaint(); // live refresh in Play Mode
    }

    static object SafeGet(Func<object> getter)
    {
        try { return getter(); }
        catch (Exception e) { return $"<exception: {e.GetType().Name}>"; }
    }

    static void DrawValue(string label, object value)
    {
        switch (value)
        {
            case null:
                EditorGUILayout.LabelField(label, "null");
                break;
            case Vector2 v2:
                EditorGUILayout.Vector2Field(label, v2); break;
            case Vector3 v3:
                EditorGUILayout.Vector3Field(label, v3); break;
            case Vector4 v4:
                EditorGUILayout.Vector4Field(label, v4); break;
            case Quaternion q:
                EditorGUILayout.Vector4Field(label, new Vector4(q.x, q.y, q.z, q.w)); break;
            case Color c:
                EditorGUILayout.ColorField(label, c); break;
            case Bounds b:
                EditorGUILayout.BoundsField(label, b); break;
            case Rect r:
                EditorGUILayout.RectField(label, r); break;
            case int i:
                EditorGUILayout.IntField(label, i); break;
            case float f:
                EditorGUILayout.FloatField(label, f); break;
            case bool b:
                EditorGUILayout.Toggle(label, b); break;
            case string s:
                EditorGUILayout.TextField(label, s); break;
            case Enum e:
                EditorGUILayout.LabelField(label, e.ToString()); break;
            default:
                EditorGUILayout.LabelField(label, value.ToString());
                break;
        }
    }
}
#endif
