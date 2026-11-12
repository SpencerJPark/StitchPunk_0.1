using System;
using System.Collections.Generic;
using UnityEngine;

// Exactly one Start node per graph. After a full play it is marked in the PlayedDialogue buffer,
// and the next trigger starts from the Refresher node instead of Start (if present).
[CreateAssetMenu(fileName = "Dialogue_New", menuName = "Dialogue/Dialogue Sequence")]
public class DialogueSequenceSO : ScriptableObject
{
    [Tooltip("Unique integer ID. Must match a constant in DialogueIds.Sequences. " +
             "Never reuse or change this value after the SO has been assigned to an NPC.")]
    public int sequenceId;

    [Tooltip("All nodes in this dialogue tree. Each node represents a step in the conversation. " +
             "Edit these through the Dialogue Editor window — do not modify the list manually.")]
    [SerializeReference]
    public List<DialogueNodeData> nodes = new List<DialogueNodeData>();

    [Tooltip("All connections between node ports. Together with the nodes list this defines the " +
             "complete conversation flow. Managed automatically by the Dialogue Editor.")]
    public List<DialogueConnectionData> connections = new List<DialogueConnectionData>();

}

[Serializable]
public abstract class DialogueNodeData
{
    [Tooltip("Unique identifier for this node within the sequence. " +
             "Generated automatically by the editor. Do not edit manually.")]
    public string nodeId = Guid.NewGuid().ToString();

    [Tooltip("Where this node sits in the Dialogue Editor canvas. " +
             "Saved automatically when you drag nodes to a new position.")]
    public Vector2 editorPosition;
}

[Serializable]
public class DialogueStartNodeData : DialogueNodeData
{
    // Output port "out"; no input ports.
}

// Played on repeat visits; without one the manager falls back to Start.
[Serializable]
public class DialogueRefresherNodeData : DialogueNodeData
{
    // Output port "out"; no input ports.
}

[Serializable]
public class DialogueEndNodeData : DialogueNodeData
{
    // Input port "in"; no output ports.
}

[Serializable]
public class DialogueLineNodeData : DialogueNodeData
{
    [Tooltip("Name of the character speaking this line. " +
             "Shown above the subtitle text bar. Leave empty to hide the speaker label entirely.")]
    public string speakerName;

    [Tooltip("The subtitle text displayed to the player when this line plays. " +
             "The player presses Interact to advance after reading it.")]
    [TextArea(3, 8)]
    public string dialogueText;

    // Ports: "in", "out".
}

[Serializable]
public class DialogueDecisionNodeData : DialogueNodeData
{
    [Tooltip("The list of choices presented to the player. " +
             "Each entry becomes one choice button and one output port in the Dialogue Editor. " +
             "Add or remove branches using the editor buttons — do not edit this list manually.")]
    public List<DialogueDecisionBranch> branches = new List<DialogueDecisionBranch>();

    // Ports: "in"; one output per branch, named by branch.portId.
}

[Serializable]
public class DialogueDecisionBranch
{
    [Tooltip("The text shown on the player's choice button for this option. " +
             "Keep it short — typically a sentence or less.")]
    public string choiceText;

    [Tooltip("Unique port identifier for this branch's output connection. " +
             "Generated automatically by the editor. Do not edit manually — " +
             "changing this will break existing connections.")]
    public string portId = Guid.NewGuid().ToString();
}

// Invisible to the player; fires eventId and continues without pausing dialogue. -1 passes through silently.
[Serializable]
public class DialogueEventNodeData : DialogueNodeData
{
    [Tooltip("The event ID fired when dialogue execution passes through this node. " +
             "Must match a constant in DialogueIds.Events (e.g., DialogueIds.Events.UnlockSomething). " +
             "Use -1 for no event — the node will pass through without triggering anything.")]
    public int eventId = -1;

    [Tooltip("Editor-only description of what this event does in the game. " +
             "Not used at runtime — just a reminder for yourself while building the graph.")]
    public string eventDescription;

    // Ports: "in", "out".
}

[Serializable]
public class DialogueConnectionData
{
    [Tooltip("The nodeId of the source node (where the connection starts).")]
    public string fromNodeId;

    [Tooltip("The port name on the source node. " +
             "'out' for Line, Event, and Start nodes. " +
             "A branch portId GUID for Decision node branches.")]
    public string fromPortId;

    [Tooltip("The nodeId of the destination node (where the connection ends).")]
    public string toNodeId;

    [Tooltip("The port name on the destination node. Always 'in'.")]
    public string toPortId = "in";
}
