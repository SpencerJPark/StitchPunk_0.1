using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One complete dialogue sequence stored as a node graph.
///
/// Assign this to a DialogueProviderAuthoring component on an NPC to wire up dialogue.
/// Open this asset in Window > Stitch Punk > Dialogue Editor to build the graph visually.
///
/// The graph must have exactly one Start node. Add one or more End nodes to close the
/// conversation. Connect nodes left-to-right to define the flow.
///
/// After a sequence plays in full it is marked in the PlayedDialogue buffer.
/// On the next trigger the refresherSequence plays instead (if assigned) and can replay freely.
/// </summary>
[CreateAssetMenu(fileName = "Dialogue_New", menuName = "Stitch Punk/Dialogue/Dialogue Sequence")]
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

    [Tooltip("An alternate shorter sequence shown on repeat visits after this primary sequence " +
             "has played once. Leave empty if this NPC has no repeat dialogue.")]
    public DialogueSequenceSO refresherSequence;
}

// ---------------------------------------------------------------------------
// Node data — abstract base and five concrete types
// ---------------------------------------------------------------------------

/// <summary>
/// Base class shared by all dialogue node types. Stores the node's unique ID and its
/// position in the editor canvas. Do not instantiate directly — use a concrete subclass.
/// </summary>
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

/// <summary>
/// Entry point for the dialogue sequence.
/// Every tree must have exactly one Start node.
/// Connect its single output port to the first Line, Decision, or Event node.
/// </summary>
[Serializable]
public class DialogueStartNodeData : DialogueNodeData
{
    // Output port name: "out"
    // No input ports.
}

/// <summary>
/// Exit point for the dialogue sequence.
/// Connect any terminal node's output port to an End node to close the conversation.
/// A tree can have multiple End nodes — one per path that ends the dialogue.
/// </summary>
[Serializable]
public class DialogueEndNodeData : DialogueNodeData
{
    // Input port name: "in"
    // No output ports.
}

/// <summary>
/// Displays a single subtitle line to the player.
/// The player presses Interact to advance to the next connected node.
///
/// Set speakerName to show a name label above the subtitle.
/// Leave speakerName empty to show only the subtitle text with no label.
/// </summary>
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

    // Input port name:  "in"  — accepts connections from any prior node
    // Output port name: "out" — single connection to the next node
}

/// <summary>
/// Presents the player with a set of labeled choice buttons.
/// Each branch in this node is one choice. Add branches in the Dialogue Editor
/// using the + Add Branch button. Each branch becomes a separate output port.
/// Connect each branch port to the node that should play when the player picks that choice.
/// </summary>
[Serializable]
public class DialogueDecisionNodeData : DialogueNodeData
{
    [Tooltip("The list of choices presented to the player. " +
             "Each entry becomes one choice button and one output port in the Dialogue Editor. " +
             "Add or remove branches using the editor buttons — do not edit this list manually.")]
    public List<DialogueDecisionBranch> branches = new List<DialogueDecisionBranch>();

    // Input port name:  "in"           — accepts connections from any prior node
    // Output port names: branch.portId — one per branch, identified by the branch's unique portId
}

/// <summary>
/// One selectable choice inside a Decision node.
/// choiceText is shown on the player's choice button.
/// portId is a unique identifier for this branch's output port (do not edit manually).
/// </summary>
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

/// <summary>
/// Fires a game event and immediately continues to the next connected node.
/// The player does not see or interact with this node — it is invisible to them.
///
/// Use Event nodes to trigger camera changes, unlock systems, update game state,
/// or any other effect that should happen mid-conversation without pausing dialogue.
///
/// The eventId must match a constant in DialogueIds.Events.
/// Use -1 for no event (passes through silently — useful as a waypoint node).
/// </summary>
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

    // Input port name:  "in"  — accepts connections from any prior node
    // Output port name: "out" — single connection to the next node
}

// ---------------------------------------------------------------------------
// Connection data
// ---------------------------------------------------------------------------

/// <summary>
/// One directed connection between an output port on one node and an input port on another.
/// Connections define how dialogue flows from step to step.
/// Managed automatically by the Dialogue Editor — do not edit manually.
/// </summary>
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
