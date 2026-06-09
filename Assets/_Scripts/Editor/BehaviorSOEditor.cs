using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(BehaviorSO))]
public class BehaviorSOEditor : Editor
{
    private SerializedProperty _behaviorTypeProp;
    private SerializedProperty _executionSequenceProp;
    private SerializedProperty _interruptionCleanupProp;

    private ReorderableList _executionList;
    private ReorderableList _interruptionList;

    private const float LINE    = 18f;
    private const float SPACING = 2f;
    private const float PAD     = 4f;

    private void OnEnable()
    {
        _behaviorTypeProp        = serializedObject.FindProperty("behaviorType");
        _executionSequenceProp   = serializedObject.FindProperty("executionSequence");
        _interruptionCleanupProp = serializedObject.FindProperty("interruptionCleanup");

        _executionList    = BuildList(_executionSequenceProp,   "Execution Sequence");
        _interruptionList = BuildList(_interruptionCleanupProp, "Interruption Cleanup");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_behaviorTypeProp);
        EditorGUILayout.Space(6);

        _executionList.DoLayoutList();
        EditorGUILayout.Space(4);
        _interruptionList.DoLayoutList();

        serializedObject.ApplyModifiedProperties();
    }

    private ReorderableList BuildList(SerializedProperty listProp, string header)
    {
        ReorderableList list = new ReorderableList(
            serializedObject, listProp,
            draggable: true, displayHeader: true,
            displayAddButton: true, displayRemoveButton: true);

        list.drawHeaderCallback = rect =>
            EditorGUI.LabelField(rect, $"{header}  ({listProp.arraySize})");

        list.onAddCallback = l =>
        {
            l.serializedProperty.arraySize++;
            SerializedProperty newEl = l.serializedProperty
                .GetArrayElementAtIndex(l.serializedProperty.arraySize - 1);
            newEl.FindPropertyRelative("type").enumValueIndex      = (int)BehaviorCommandType.WaitTime;
            newEl.FindPropertyRelative("IntParam").intValue        = 0;
            newEl.FindPropertyRelative("FloatParam").floatValue    = 0f;
            newEl.FindPropertyRelative("Duration").floatValue      = 1f;
        };

        list.elementHeightCallback = index =>
        {
            SerializedProperty element  = listProp.GetArrayElementAtIndex(index);
            BehaviorCommandType cmdType = (BehaviorCommandType)
                element.FindPropertyRelative("type").enumValueIndex;
            int lines = GetLineCount(cmdType);
            return LINE * lines + SPACING * (lines - 1) + PAD * 2;
        };

        list.drawElementCallback = (rect, index, active, focused) =>
            DrawCommand(rect, listProp.GetArrayElementAtIndex(index));

        return list;
    }

    private static int GetLineCount(BehaviorCommandType type)
    {
        switch (type)
        {
            case BehaviorCommandType.RequestAttack:
            case BehaviorCommandType.RequestPickup:
            case BehaviorCommandType.FleeFromTarget:
                return 1;

            case BehaviorCommandType.WaitTime:
            case BehaviorCommandType.ReleaseInteraction:
                return 2;

            default:
                return 3;
        }
    }

    private void DrawCommand(Rect rect, SerializedProperty element)
    {
        SerializedProperty typeProp     = element.FindPropertyRelative("type");
        SerializedProperty intProp      = element.FindPropertyRelative("IntParam");
        SerializedProperty floatProp    = element.FindPropertyRelative("FloatParam");
        SerializedProperty durationProp = element.FindPropertyRelative("Duration");

        float y   = rect.y + PAD;
        Rect  row = new Rect(rect.x, y, rect.width, LINE);

        EditorGUI.PropertyField(row, typeProp, new GUIContent("Type"));
        y += LINE + SPACING;
        row.y = y;

        BehaviorCommandType cmdType = (BehaviorCommandType)typeProp.enumValueIndex;

        switch (cmdType)
        {
            case BehaviorCommandType.Approach:
                DrawEnumAsInt<StanceType>(intProp, "Stance", row);
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, floatProp, new GUIContent("Stopping Dist"));
                break;

            case BehaviorCommandType.WaitTime:
                EditorGUI.PropertyField(row, durationProp, new GUIContent("Duration"));
                break;

            case BehaviorCommandType.RequestAttack:
            case BehaviorCommandType.RequestPickup:
            case BehaviorCommandType.FleeFromTarget:
                break;

            case BehaviorCommandType.ModifyMotivation:
                DrawEnumAsInt<NeedType>(intProp, "Need", row);
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, floatProp, new GUIContent("Delta"));
                break;

            case BehaviorCommandType.PlayAnimation:
                DrawAnimationEnumAsInt(intProp, row);
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, floatProp, new GUIContent("Speed"));
                break;

            case BehaviorCommandType.ReleaseInteraction:
                EditorGUI.PropertyField(row, floatProp, new GUIContent("Cooldown (s)"));
                break;

            case BehaviorCommandType.ModifyStat:
                EditorGUI.PropertyField(row, intProp, new GUIContent("Stat ID"));
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, floatProp, new GUIContent("Delta"));
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, durationProp, new GUIContent("Duration"));
                break;

            case BehaviorCommandType.SpawnEntity:
                EditorGUI.PropertyField(row, intProp, new GUIContent("Entity Type"));
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, durationProp, new GUIContent("Duration"));
                break;

            case BehaviorCommandType.StartDialogue:
                EditorGUI.PropertyField(row, intProp, new GUIContent("Node ID"));
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, durationProp, new GUIContent("Duration"));
                break;

            case BehaviorCommandType.ApplyForce:
                EditorGUI.PropertyField(row, intProp, new GUIContent("Force Type"));
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, floatProp, new GUIContent("Magnitude"));
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, durationProp, new GUIContent("Duration"));
                break;
        }
    }

    private static void AdvanceRow(ref float y, ref Rect row)
    {
        y     += LINE + SPACING;
        row.y  = y;
    }

    private static void DrawEnumAsInt<TEnum>(SerializedProperty intProp, string label, Rect rect)
        where TEnum : Enum
    {
        TEnum current = (TEnum)(object)intProp.intValue;
        TEnum next    = (TEnum)EditorGUI.EnumPopup(rect, new GUIContent(label), current);
        if (!next.Equals(current))
            intProp.intValue = (int)(object)next;
    }

    private static void DrawAnimationEnumAsInt(SerializedProperty intProp, Rect rect)
    {
        AnimationType current    = (AnimationType)(ushort)intProp.intValue;
        Rect          buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent("Animation"));

        if (GUI.Button(buttonRect, current.ToString(), EditorStyles.popup))
        {
            var obj  = intProp.serializedObject;
            var path = intProp.propertyPath;
            new EnumSearchDropdown(
                new AdvancedDropdownState(),
                Enum.GetNames(typeof(AnimationType)),
                idx =>
                {
                    SerializedProperty prop = obj.FindProperty(path);
                    prop.intValue = idx;
                    obj.ApplyModifiedProperties();
                }
            ).Show(buttonRect);
        }
    }
}
