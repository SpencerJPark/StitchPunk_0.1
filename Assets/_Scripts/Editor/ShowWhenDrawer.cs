using UnityEditor;
using UnityEngine;

// Drawer for [ShowWhen] — collapses the decorated field to zero height while its sibling bool
// condition doesn't match. The condition is looked up as a SIBLING of the decorated field (same
// serialized parent), so it works inside nested classes and list elements. A missing or non-bool
// condition field draws the property normally rather than silently hiding data.
[CustomPropertyDrawer(typeof(ShowWhenAttribute))]
public class ShowWhenDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return IsShown(property)
            ? EditorGUI.GetPropertyHeight(property, label, true)
            : -EditorGUIUtility.standardVerticalSpacing; // cancel the row spacing too
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (!IsShown(property))
            return;

        EditorGUI.PropertyField(position, property, label, true);
    }

    private bool IsShown(SerializedProperty property)
    {
        ShowWhenAttribute showWhen = (ShowWhenAttribute)attribute;

        string propertyPath = property.propertyPath;
        int lastSeparator = propertyPath.LastIndexOf('.');
        string conditionPath = lastSeparator >= 0
            ? propertyPath.Substring(0, lastSeparator + 1) + showWhen.conditionField
            : showWhen.conditionField;

        SerializedProperty conditionProperty = property.serializedObject.FindProperty(conditionPath);
        if (conditionProperty == null || conditionProperty.propertyType != SerializedPropertyType.Boolean)
            return true; // misconfigured condition — show the field instead of hiding data

        return conditionProperty.boolValue == showWhen.shownWhen;
    }
}
