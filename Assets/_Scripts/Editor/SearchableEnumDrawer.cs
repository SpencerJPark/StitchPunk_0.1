using System;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

[CustomPropertyDrawer(typeof(SearchableEnumAttribute))]
public class SearchableEnumDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.Enum)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        EditorGUI.BeginProperty(position, label, property);

        Rect buttonRect   = EditorGUI.PrefixLabel(position, label);
        int  currentIndex = property.enumValueIndex;
        string[] names    = property.enumDisplayNames;

        string buttonLabel = currentIndex >= 0 && currentIndex < names.Length
            ? names[currentIndex]
            : "— select —";

        if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
        {
            // Capture property path so the lambda works correctly in batched repaints
            var obj  = property.serializedObject;
            var path = property.propertyPath;

            var dropdown = new EnumSearchDropdown(
                new AdvancedDropdownState(),
                names,
                selectedIndex =>
                {
                    if (selectedIndex < 0 || selectedIndex >= names.Length)
                        return;

                    SerializedProperty prop = obj.FindProperty(path);
                    prop.enumValueIndex = selectedIndex;
                    obj.ApplyModifiedProperties();
                });

            dropdown.Show(buttonRect);
        }

        EditorGUI.EndProperty();
    }
}

/// <summary>
/// AdvancedDropdown subclass that lists enum entries with built-in search.
/// </summary>
class EnumSearchDropdown : AdvancedDropdown
{
    private readonly string[] names;
    private readonly Action<int> onSelected;

    public EnumSearchDropdown(AdvancedDropdownState state, string[] names, Action<int> onSelected)
        : base(state)
    {
        this.names      = names;
        this.onSelected = onSelected;
        minimumSize     = new Vector2(200f, 250f);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        AdvancedDropdownItem root = new AdvancedDropdownItem("Select");
        for (int index = 0; index < names.Length; index++)
            root.AddChild(new AdvancedDropdownItem(names[index]) { id = index });
        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        // Search mode rebuilds the item tree, so item.id is unreliable — resolve
        // the enum index by matching the displayed name back to our names array.
        int resolvedIndex = Array.IndexOf(names, item.name);
        if (resolvedIndex < 0)
            resolvedIndex = item.id;

        onSelected(resolvedIndex);
    }
}
