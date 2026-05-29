using UnityEditor;

[CustomEditor(typeof(ItemSO))]
public class ItemSOEditor : Editor
{
    private SerializedProperty itemTypeProp;
    private SerializedProperty categoryProp;
    private SerializedProperty weaponAttackProp;
    private SerializedProperty onHitEffectProp;
    private SerializedProperty consumeEffectProp;
    private SerializedProperty pickupRangeProp;
    private SerializedProperty consumeDurationProp;
    private SerializedProperty baseUtilityProp;

    private void OnEnable()
    {
        itemTypeProp        = serializedObject.FindProperty("itemType");
        categoryProp        = serializedObject.FindProperty("category");
        weaponAttackProp    = serializedObject.FindProperty("weaponAttack");
        onHitEffectProp     = serializedObject.FindProperty("onHitEffect");
        consumeEffectProp   = serializedObject.FindProperty("consumeEffect");
        pickupRangeProp     = serializedObject.FindProperty("pickupRange");
        consumeDurationProp = serializedObject.FindProperty("consumeDuration");
        baseUtilityProp     = serializedObject.FindProperty("baseUtility");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(itemTypeProp);
        EditorGUILayout.PropertyField(categoryProp);

        ItemCategory category = (ItemCategory)categoryProp.enumValueIndex;

        switch (category)
        {
            case ItemCategory.Weapon:
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Weapon", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(weaponAttackProp);
                EditorGUILayout.PropertyField(onHitEffectProp);
                break;

            case ItemCategory.Consumable:
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Consumable", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(consumeEffectProp);
                break;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pickup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(pickupRangeProp);
        EditorGUILayout.PropertyField(consumeDurationProp);
        EditorGUILayout.PropertyField(baseUtilityProp);

        serializedObject.ApplyModifiedProperties();
    }
}
