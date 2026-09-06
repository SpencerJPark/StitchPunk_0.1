#if UNITY_EDITOR
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// The game's half of the toolkit's cutscene event payload seam: a Dialogue cue carries a sequence id
// in intParam and a speaker slot index in floatParam (G2 §4), and neither reads as anything but a
// number until a host says what they mean. Registered the way UnitDirectionSetContextProvider is.
public sealed class CutsceneDialogueEventInspectorProvider : ICutsceneEventInspectorProvider
{
    private const string NoSpeakerLabel = "(no speaker)";

    [InitializeOnLoadMethod]
    private static void RegisterWithTheToolkit()
    {
        CutsceneEventInspectorProviders.Register(new CutsceneDialogueEventInspectorProvider());
    }

    public bool TryBuildInspector(uint eventKey, SerializedProperty markerProperty, VisualElement container)
    {
        if (eventKey != AnimEvents.Dialogue)
        {
            return false;
        }

        container.Add(BuildSequenceField(markerProperty.FindPropertyRelative("intParam")));
        container.Add(BuildSpeakerField(markerProperty, markerProperty.FindPropertyRelative("floatParam")));
        return true;
    }

    // The sequence is picked as an asset and stored as its id, so renaming or moving the SO cannot
    // repoint the cue — the same identity-by-id rule the rest of this vocabulary follows.
    private static VisualElement BuildSequenceField(SerializedProperty sequenceIdProperty)
    {
        ObjectField sequenceField = new ObjectField("Dialogue Sequence")
        {
            objectType = typeof(DialogueSequenceSO),
            allowSceneObjects = false,
        };
        sequenceField.SetValueWithoutNotify(FindSequenceById(sequenceIdProperty.intValue));

        sequenceField.RegisterValueChangedCallback(changeEvent =>
        {
            DialogueSequenceSO chosenSequence = changeEvent.newValue as DialogueSequenceSO;
            sequenceIdProperty.intValue = chosenSequence != null ? chosenSequence.sequenceId : -1;
            sequenceIdProperty.serializedObject.ApplyModifiedProperties();
        });

        return sequenceField;
    }

    private static VisualElement BuildSpeakerField(
        SerializedProperty markerProperty, SerializedProperty speakerSlotProperty)
    {
        List<string> slotChoices = new List<string> { NoSpeakerLabel };
        CutsceneAsset cutscene = markerProperty.serializedObject.targetObject as CutsceneAsset;
        if (cutscene != null)
        {
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                slotChoices.Add(MakeUniqueSlotLabel(slotChoices, cutscene.slots[slotIndex].name, slotIndex));
            }
        }

        // Choice 0 is "nobody", so the stored index is one less than the chosen one — which is what
        // lets -1 mean "no speaker" in a float payload that has no other way to say it.
        DropdownField speakerField = new DropdownField("Speaker", slotChoices, 0);
        int storedSlotIndex = (int)speakerSlotProperty.floatValue;
        speakerField.SetValueWithoutNotify(
            storedSlotIndex >= 0 && storedSlotIndex < slotChoices.Count - 1
                ? slotChoices[storedSlotIndex + 1]
                : NoSpeakerLabel);

        // Resolved from the chosen label, which is what the event carries, rather than from
        // DropdownField.index — the labels are made unique above so the lookup cannot be ambiguous.
        speakerField.RegisterValueChangedCallback(changeEvent =>
        {
            speakerSlotProperty.floatValue = slotChoices.IndexOf(changeEvent.newValue) - 1;
            speakerSlotProperty.serializedObject.ApplyModifiedProperties();
        });

        return speakerField;
    }

    private static string MakeUniqueSlotLabel(List<string> existingChoices, string slotName, int slotIndex)
    {
        string label = string.IsNullOrEmpty(slotName) ? "Slot " + slotIndex : slotName;
        return existingChoices.Contains(label) ? label + " (slot " + slotIndex + ")" : label;
    }

    private static DialogueSequenceSO FindSequenceById(int sequenceId)
    {
        if (sequenceId < 0)
        {
            return null;
        }

        string[] sequenceGuids = AssetDatabase.FindAssets("t:DialogueSequenceSO");
        for (int guidIndex = 0; guidIndex < sequenceGuids.Length; guidIndex++)
        {
            DialogueSequenceSO sequence = AssetDatabase.LoadAssetAtPath<DialogueSequenceSO>(
                AssetDatabase.GUIDToAssetPath(sequenceGuids[guidIndex]));
            if (sequence != null && sequence.sequenceId == sequenceId)
            {
                return sequence;
            }
        }
        return null;
    }
}
#endif
