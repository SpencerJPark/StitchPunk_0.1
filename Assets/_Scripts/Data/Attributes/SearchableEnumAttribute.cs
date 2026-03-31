using UnityEngine;

/// <summary>
/// Apply to any serialized enum field to replace the default dropdown
/// with a searchable popup. Works on MonoBehaviours and ScriptableObjects.
///
/// Usage:
///   [SearchableEnum]
///   public AttackType attackType;
/// </summary>
public class SearchableEnumAttribute : PropertyAttribute { }
