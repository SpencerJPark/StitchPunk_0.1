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
            newEl.FindPropertyRelative("type").enumValueIndex            = (int)BehaviorCommandType.WaitTime;
            newEl.FindPropertyRelative("IntParam").intValue              = 0;
            newEl.FindPropertyRelative("FloatParam").floatValue          = 0f;
            newEl.FindPropertyRelative("Duration").floatValue            = 1f;
            newEl.FindPropertyRelative("Qualifier").intValue             = 0;
            newEl.FindPropertyRelative("QualifierIntParam").intValue     = 0;
            newEl.FindPropertyRelative("QualifierFloatParam").floatValue = 0f;
            newEl.FindPropertyRelative("Looping").boolValue              = false;
        };

        list.elementHeightCallback = index =>
        {
            int lines = GetElementLines(listProp, index);
            return LINE * lines + SPACING * (lines - 1) + PAD * 2;
        };

        list.drawElementCallback = (rect, index, active, focused) =>
            DrawCommand(rect, listProp.GetArrayElementAtIndex(index), index, listProp);

        return list;
    }

    private static int GetElementLines(SerializedProperty listProp, int index)
    {
        SerializedProperty element  = listProp.GetArrayElementAtIndex(index);
        BehaviorCommandType cmdType = (BehaviorCommandType)
            element.FindPropertyRelative("type").enumValueIndex;

        if (cmdType == BehaviorCommandType.LoopUntil)
        {
            // Type + Jump To Index + Qualifier + Timeout
            int lines = 4;
            LoopQualifier qualifier = (LoopQualifier)element.FindPropertyRelative("Qualifier").intValue;
            if ((qualifier & LoopQualifier.TargetOutOfRange) != 0)    lines += 1; // Range
            if ((qualifier & LoopQualifier.MotivationSatisfied) != 0) lines += 2; // Need + Threshold
            if (GetLoopUntilWarning(element, index, listProp) != null) lines += 2; // HelpBox
            return lines;
        }

        return GetLineCount(cmdType);
    }

    // Returns null when the LoopUntil element is valid; otherwise the warning text.
    private static string GetLoopUntilWarning(SerializedProperty element, int index, SerializedProperty listProp)
    {
        int jumpIndex = element.FindPropertyRelative("IntParam").intValue;
        LoopQualifier qualifier = (LoopQualifier)element.FindPropertyRelative("Qualifier").intValue;
        float timeout = element.FindPropertyRelative("Duration").floatValue;

        if (jumpIndex < 0 || jumpIndex >= index)
            return $"Jump To Index must be >= 0 and < {index} (this command's index). Bakes as a no-op.";

        if (qualifier == LoopQualifier.None && timeout <= 0f)
            return "No exit condition: tick a Qualifier flag or set a Timeout. " +
                   "Loop will run until the global 60s default fires.";

        for (int i = jumpIndex; i < index; i++)
        {
            SerializedProperty other = listProp.GetArrayElementAtIndex(i);
            BehaviorCommandType otherType = (BehaviorCommandType)
                other.FindPropertyRelative("type").enumValueIndex;
            if (otherType == BehaviorCommandType.LoopUntil)
                return $"Loop body encloses another LoopUntil at [{i}] — nested loops are unsupported " +
                       "(single LoopTimer per unit).";
        }

        return null;
    }

    private static int GetLineCount(BehaviorCommandType type)
    {
        switch (type)
        {
            case BehaviorCommandType.RequestAttack:
            case BehaviorCommandType.RequestPickup:
            case BehaviorCommandType.FleeFromTarget:
            case BehaviorCommandType.StopAnimation:
                return 1;

            case BehaviorCommandType.WaitTime:
            case BehaviorCommandType.ReleaseInteraction:
                return 2;

            case BehaviorCommandType.PlayAnimation:
                return 4; // Type + Animation + Speed + Looping

            case BehaviorCommandType.PlayActionAnimation:
                return 3; // Type + Speed + Looping (animation resolved per-unit at runtime)

            default:
                return 3;
        }
    }

    private void DrawCommand(Rect rect, SerializedProperty element, int index, SerializedProperty listProp)
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
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, element.FindPropertyRelative("Looping"),
                    new GUIContent("Looping", "Loop the clip until StopAnimation or interrupt cleanup"));
                break;

            case BehaviorCommandType.StopAnimation:
                break;

            case BehaviorCommandType.PlayActionAnimation:
                EditorGUI.PropertyField(row, floatProp, new GUIContent("Speed",
                    "Playback speed; 0 = 1x. Animation itself is resolved per-unit from its action mapping."));
                AdvanceRow(ref y, ref row);
                EditorGUI.PropertyField(row, element.FindPropertyRelative("Looping"),
                    new GUIContent("Looping"));
                break;

            case BehaviorCommandType.LoopUntil:
            {
                SerializedProperty qualifierProp = element.FindPropertyRelative("Qualifier");

                EditorGUI.PropertyField(row, intProp, new GUIContent("Jump To Index",
                    "Command index to jump back to while no exit condition holds"));
                AdvanceRow(ref y, ref row);

                LoopQualifier qualifier = (LoopQualifier)qualifierProp.intValue;
                LoopQualifier nextQualifier = (LoopQualifier)EditorGUI.EnumFlagsField(
                    row, new GUIContent("Exit When (any)"), qualifier);
                if (nextQualifier != qualifier)
                    qualifierProp.intValue = (int)nextQualifier;
                AdvanceRow(ref y, ref row);

                if ((nextQualifier & LoopQualifier.TargetOutOfRange) != 0)
                {
                    EditorGUI.PropertyField(row, floatProp, new GUIContent("Range (m)"));
                    AdvanceRow(ref y, ref row);
                }

                if ((nextQualifier & LoopQualifier.MotivationSatisfied) != 0)
                {
                    DrawEnumAsInt<NeedType>(element.FindPropertyRelative("QualifierIntParam"), "Need", row);
                    AdvanceRow(ref y, ref row);
                    EditorGUI.PropertyField(row, element.FindPropertyRelative("QualifierFloatParam"),
                        new GUIContent("Threshold"));
                    AdvanceRow(ref y, ref row);
                }

                EditorGUI.PropertyField(row, durationProp, new GUIContent("Timeout (s)",
                    "Loop safety timeout; 0 = global 60s default"));
                AdvanceRow(ref y, ref row);

                string warning = GetLoopUntilWarning(element, index, listProp);
                if (warning != null)
                {
                    Rect helpRect = new Rect(rect.x, y, rect.width, LINE * 2);
                    EditorGUI.HelpBox(helpRect, warning, MessageType.Error);
                }
                break;
            }

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
