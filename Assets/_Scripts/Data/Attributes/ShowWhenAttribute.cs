using UnityEngine;

// Hides the field unless a SIBLING bool has the given value. Resolved next to the field, so it
// works in nested serializable types and list elements. Usage: [ShowWhen("useFullRange", false)]
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
