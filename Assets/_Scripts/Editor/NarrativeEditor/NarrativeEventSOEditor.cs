#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NarrativeEventSO))]
public class NarrativeEventSOEditor : Editor
{
    private static readonly (string label, Type type)[] ActionTypes =
    {
        ("Dialogue Trigger", typeof(DialogueTriggerAction)),
        ("Move NPC",         typeof(MoveNPCAction)),
        ("Play Animation",   typeof(PlayAnimationAction)),
        ("Enable Component", typeof(EnableComponentAction)),
        ("Cinematic Camera", typeof(CinematicCameraAction)),
        ("Zombify",          typeof(ZombifyAction)),
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        NarrativeEventSO so = (NarrativeEventSO)target;

        // ── Top-level fields ───────────────────────────────────────────────
        EditorGUILayout.PropertyField(serializedObject.FindProperty("eventId"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blockPlayerInput"));
        EditorGUILayout.Space(10f);

        // ── Sequence overview diagram ──────────────────────────────────────
        DrawSequenceOverview(so);

        // ── Groups (editable) ──────────────────────────────────────────────
        SerializedProperty groupsProp = serializedObject.FindProperty("groups");
        EditorGUILayout.LabelField("Action Groups", EditorStyles.boldLabel);
        EditorGUILayout.Space(2f);

        int groupToDelete = -1;

        for (int g = 0; g < groupsProp.arraySize; g++)
        {
            int capturedGroup = g;
            SerializedProperty groupProp   = groupsProp.GetArrayElementAtIndex(g);
            SerializedProperty nameProp    = groupProp.FindPropertyRelative("groupName");
            SerializedProperty actionsProp = groupProp.FindPropertyRelative("actions");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Group header row
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{g}:", GUILayout.Width(18f));
            nameProp.stringValue = EditorGUILayout.TextField(nameProp.stringValue);
            string countLabel = actionsProp.arraySize == 1 ? "1 action" : $"{actionsProp.arraySize} actions";
            EditorGUILayout.LabelField(countLabel, EditorStyles.miniLabel, GUILayout.Width(58f));
            if (GUILayout.Button("Remove", GUILayout.Width(58f)))
                groupToDelete = g;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            // Actions
            int actionToDelete = -1;
            for (int a = 0; a < actionsProp.arraySize; a++)
            {
                SerializedProperty actionProp = actionsProp.GetArrayElementAtIndex(a);
                string typeName = actionProp.managedReferenceValue != null
                    ? ObjectNames.NicifyVariableName(actionProp.managedReferenceValue.GetType().Name)
                    : "Null";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(typeName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", GUILayout.Width(22f)))
                    actionToDelete = a;
                EditorGUILayout.EndHorizontal();

                if (actionProp.managedReferenceValue != null)
                {
                    EditorGUI.indentLevel++;
                    DrawActionFields(actionProp, (NarrativeActionBase)actionProp.managedReferenceValue);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            if (actionToDelete >= 0)
                actionsProp.DeleteArrayElementAtIndex(actionToDelete);

            // Add Action dropdown
            EditorGUILayout.Space(2f);
            Rect addRect = GUILayoutUtility.GetRect(new GUIContent("+ Add Action"), GUI.skin.button);
            if (GUI.Button(addRect, "+ Add Action"))
            {
                GenericMenu menu = new GenericMenu();
                foreach ((string label, Type type) in ActionTypes)
                {
                    Type capturedType = type;
                    menu.AddItem(new GUIContent(label), false, () =>
                    {
                        serializedObject.Update();
                        SerializedProperty ap = serializedObject
                            .FindProperty("groups")
                            .GetArrayElementAtIndex(capturedGroup)
                            .FindPropertyRelative("actions");
                        ap.arraySize++;
                        ap.GetArrayElementAtIndex(ap.arraySize - 1).managedReferenceValue =
                            Activator.CreateInstance(capturedType);
                        serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.ShowAsContext();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        if (groupToDelete >= 0)
            groupsProp.DeleteArrayElementAtIndex(groupToDelete);

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("+ Add Group", GUILayout.Height(24f)))
        {
            groupsProp.arraySize++;
            SerializedProperty newGroup = groupsProp.GetArrayElementAtIndex(groupsProp.arraySize - 1);
            newGroup.FindPropertyRelative("groupName").stringValue = "";
            newGroup.FindPropertyRelative("actions").ClearArray();
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Sequence overview ──────────────────────────────────────────────────

    private static void DrawSequenceOverview(NarrativeEventSO so)
    {
        if (so.groups == null || so.groups.Count == 0) return;

        EditorGUILayout.LabelField("Sequence Overview", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        for (int g = 0; g < so.groups.Count; g++)
        {
            NarrativeActionGroup group = so.groups[g];
            string name = string.IsNullOrWhiteSpace(group.groupName) ? $"Group {g}" : group.groupName;

            EditorGUILayout.LabelField(name, EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            foreach (NarrativeActionBase action in group.actions)
            {
                string pill = GetActionSummary(action);
                GUILayout.Box(pill, EditorStyles.helpBox, GUILayout.ExpandWidth(false));
            }
            if (group.actions.Count == 0)
                EditorGUILayout.LabelField("(no actions)", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndHorizontal();

            if (g < so.groups.Count - 1)
                EditorGUILayout.LabelField("↓", EditorStyles.centeredGreyMiniLabel);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(8f);
    }

    private static string GetActionSummary(NarrativeActionBase action)
    {
        switch (action)
        {
            case MoveNPCAction m:
                return $"Move #{m.targetEntityId} → #{m.waypointEntityId}";
            case PlayAnimationAction p:
                string wait = p.waitForCompletion ? " ⏳" : p.duration > 0f ? $" {p.duration}s" : "";
                return $"{(p.animationClip != null ? p.animationClip.name : "(none)")} [{p.layer}]{wait}";
            case DialogueTriggerAction d:
                return $"Dialogue: {d.sequence?.name ?? "(none)"}";
            case EnableComponentAction e:
                return $"{(e.enable ? "Enable" : "Disable")} {e.componentType}";
            case CinematicCameraAction c:
                return $"Camera {c.holdDuration}s";
            case ZombifyAction z:
                string zombifyTarget = z.targetUnitType == UnitType.None ? "authored form" : z.targetUnitType.ToString();
                return $"Zombify #{z.targetEntityId} → {zombifyTarget}{(z.delaySeconds > 0f ? $" ({z.delaySeconds}s)" : "")}";
            default:
                return action.GetType().Name;
        }
    }

    // ── Field rendering ────────────────────────────────────────────────────

    // NextVisible does not enter [SerializeReference] managed references in Unity.
    // Use FindPropertyRelative by explicit field name instead — it navigates by path, not iterator.
    private static void DrawActionFields(SerializedProperty actionProp, NarrativeActionBase action)
    {
        DrawFieldRelative(actionProp, "targetEntityId");

        switch (action)
        {
            case DialogueTriggerAction _:
                DrawFieldRelative(actionProp, "sequence");
                break;
            case MoveNPCAction _:
                DrawFieldRelative(actionProp, "waypointEntityId");
                break;
            case PlayAnimationAction _:
                DrawFieldRelative(actionProp, "animationClip");
                DrawFieldRelative(actionProp, "layer");
                DrawFieldRelative(actionProp, "waitForCompletion");
                DrawFieldRelative(actionProp, "duration");
                DrawFieldRelative(actionProp, "looping");
                break;
            case EnableComponentAction _:
                DrawFieldRelative(actionProp, "componentType");
                DrawFieldRelative(actionProp, "enable");
                break;
            case CinematicCameraAction _:
                DrawFieldRelative(actionProp, "holdDuration");
                DrawFieldRelative(actionProp, "targetOffset");
                break;
            case ZombifyAction _:
                DrawFieldRelative(actionProp, "targetUnitType");
                DrawFieldRelative(actionProp, "delaySeconds");
                DrawFieldRelative(actionProp, "waitForConversion");
                break;
        }
    }

    private static void DrawFieldRelative(SerializedProperty parent, string fieldName)
    {
        SerializedProperty prop = parent.FindPropertyRelative(fieldName);
        if (prop != null)
            EditorGUILayout.PropertyField(prop, true);
    }
}
#endif
