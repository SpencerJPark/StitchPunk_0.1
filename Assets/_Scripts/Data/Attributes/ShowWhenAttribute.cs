using UnityEngine;

/// <summary>
/// Apply to any serialized field to show it in the inspector only while a SIBLING bool field has
/// the given value — the field is hidden entirely (no disabled greyout) otherwise. Works inside
/// nested [Serializable] classes/structs and list elements (the condition is resolved next to the
/// decorated field, not on the root object).
///
/// Usage:
///   public bool useFullRange = true;
///   [ShowWhen("useFullRange", false)]   // visible only while useFullRange is UNCHECKED
///   public int minColorIndex;
///
///   public bool hasAlternative;
///   [ShowWhen("hasAlternative")]        // visible only while hasAlternative is CHECKED
///   public Color alternative;
/// </summary>
public class ShowWhenAttribute : PropertyAttribute
{
    public readonly string conditionField;
    public readonly bool shownWhen;

    public ShowWhenAttribute(string conditionField, bool shownWhen = true)
    {
        this.conditionField = conditionField;
        this.shownWhen = shownWhen;
    }
}
