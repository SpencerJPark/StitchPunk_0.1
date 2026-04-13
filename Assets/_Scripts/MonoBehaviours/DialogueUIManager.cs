using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the subtitle dialogue UI. Walks the DialogueSequenceSO node graph and
/// shows the appropriate UI for each node type the player reaches.
///
/// How it works:
///   DialogueStartSystem (ECS) detects the player interacting near a DialogueProvider NPC
///   and enables ActiveDialogue on the manager entity with the sequence ID to play.
///
///   Each Update() this MonoBehaviour checks whether ActiveDialogue just became enabled.
///   When a new sequence starts it builds two lookup tables from the SO, finds the Start
///   node, then calls DisplayNode() to walk through the graph:
///
///   - Line node:      Show speaker name + subtitle text. Wait for the player to press
///                     Interact, then follow the "out" connection to the next node.
///   - Decision node:  Show choice buttons (one per branch). When the player picks one,
///                     follow that branch's output connection to the next node.
///   - Event node:     Fire the event ID via OnDialogueEvent, then immediately follow
///                     "out" without waiting for player input.
///   - End node:       Close the dialogue panel and mark the sequence as played.
///
/// Registry:
///   Populate sequenceAssets in the inspector with every DialogueSequenceSO used in
///   this scene (including refreshers). A Dictionary is built at Awake for O(1) lookup.
///
/// Choice buttons:
///   Wire up as many Button + Label pairs in choiceButtons / choiceButtonLabels as the
///   maximum number of Decision branches you expect in this scene. Unused buttons are
///   hidden automatically. Branches beyond the wired button count are silently skipped.
/// </summary>
public class DialogueUIManager : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector references
    // ------------------------------------------------------------------

    [Header("Canvas References")]
    [Tooltip("Root panel shown when dialogue is active. Hidden when dialogue ends.")]
    [SerializeField] private GameObject dialoguePanel;

    [Tooltip("TextMeshPro label that shows the speaker's name above the subtitle. " +
             "The GameObject is hidden automatically when speakerName is empty.")]
    [SerializeField] private TextMeshProUGUI speakerNameLabel;

    [Tooltip("TextMeshPro label that shows the subtitle text for Line nodes.")]
    [SerializeField] private TextMeshProUGUI subtitleLabel;

    [Tooltip("Root object holding the choice buttons. Shown only when a Decision node is active.")]
    [SerializeField] private GameObject choiceGroup;

    [Tooltip("Choice buttons — one per Decision branch the player can select. " +
             "Wire up as many as the highest branch count you plan to use. Extras are hidden.")]
    [SerializeField] private Button[] choiceButtons;

    [Tooltip("TextMeshPro labels on the choice buttons. Index must match choiceButtons exactly.")]
    [SerializeField] private TextMeshProUGUI[] choiceButtonLabels;

    [Header("Dialogue Assets")]
    [Tooltip("Every DialogueSequenceSO used in this scene, including refreshers. " +
             "The manager builds a Dictionary<int, SO> from this list at Awake for fast lookup.")]
    [SerializeField] private List<DialogueSequenceSO> sequenceAssets;

    // ------------------------------------------------------------------
    // ECS references (resolved in Start)
    // ------------------------------------------------------------------

    private EntityManager entityManager;
    private Entity managerEntity;
    private Entity playerEntity;
    private Entity gameDataEntity;

    // ------------------------------------------------------------------
    // Asset registry
    // ------------------------------------------------------------------

    private Dictionary<int, DialogueSequenceSO> sequenceRegistry = new Dictionary<int, DialogueSequenceSO>();

    // ------------------------------------------------------------------
    // Node graph lookup tables (rebuilt each time a new sequence starts)
    // ------------------------------------------------------------------

    /// <summary>Maps nodeId → node data for the currently playing sequence.</summary>
    private Dictionary<string, DialogueNodeData> nodeById = new Dictionary<string, DialogueNodeData>();

    /// <summary>
    /// Maps (fromNodeId, fromPortId) → toNodeId for the currently playing sequence.
    /// Used to follow any output port to the next node in one lookup.
    /// </summary>
    private Dictionary<(string nodeId, string portId), string> connectionMap =
        new Dictionary<(string, string), string>();

    // ------------------------------------------------------------------
    // Runtime state
    // ------------------------------------------------------------------

    private DialogueSequenceSO currentSequence;
    private string currentNodeId;
    private bool awaitingChoice;
    private int activeSequenceId = -1;  // -1 means no dialogue is displayed

    // Captures the current Decision node's branches while awaiting a choice,
    // so the button callbacks know which branch portId to follow.
    private List<DialogueDecisionBranch> pendingBranches = new List<DialogueDecisionBranch>();

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        foreach (DialogueSequenceSO sequenceSO in sequenceAssets)
        {
            if (sequenceSO == null) continue;
            sequenceRegistry[sequenceSO.sequenceId] = sequenceSO;
        }

        dialoguePanel.SetActive(false);
        choiceGroup.SetActive(false);

        // Wire choice button callbacks. The captured index is used to pick the branch.
        for (int buttonIndex = 0; buttonIndex < choiceButtons.Length; buttonIndex++)
        {
            int capturedIndex = buttonIndex;
            choiceButtons[buttonIndex].onClick.AddListener(() => OnChoiceSelected(capturedIndex));
        }
    }

    private void Start()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            Debug.LogError("DialogueUIManager: No default DOTS world found.");
            return;
        }

        entityManager = world.EntityManager;

        EntityQuery managerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<DialogueManagerTag>());
        if (managerQuery.IsEmpty)
        {
            Debug.LogError("DialogueUIManager: No DialogueManagerAuthoring entity found in the scene.");
            return;
        }
        managerEntity = managerQuery.GetSingletonEntity();

        EntityQuery playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        if (playerQuery.IsEmpty)
        {
            Debug.LogError("DialogueUIManager: No Player entity found.");
            return;
        }
        playerEntity = playerQuery.GetSingletonEntity();

        EntityQuery gameDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameDataTag>());
        if (gameDataQuery.IsEmpty)
        {
            Debug.LogError("DialogueUIManager: No GameDataTag entity found.");
            return;
        }
        gameDataEntity = gameDataQuery.GetSingletonEntity();
    }

    private void Update()
    {
        if (managerEntity == Entity.Null || playerEntity == Entity.Null) return;

        bool isDialogueActive = entityManager.IsComponentEnabled<ActiveDialogue>(managerEntity);

        if (isDialogueActive)
        {
            ActiveDialogue activeDialogue = entityManager.GetComponentData<ActiveDialogue>(managerEntity);

            // Detect a fresh dialogue start (new sequence ID set by DialogueStartSystem).
            if (activeDialogue.sequenceId != activeSequenceId)
            {
                BeginSequence(activeDialogue.sequenceId);
                return;
            }

            // While waiting at a Line node, advance on Interact press.
            if (!awaitingChoice && ConsumeInteractInput())
            {
                AdvanceFromCurrentNode("out");
            }
        }
        else if (activeSequenceId != -1)
        {
            // ActiveDialogue was disabled externally — clean up without marking as played.
            CloseDialoguePanel();
        }
    }

    // ------------------------------------------------------------------
    // Sequence lifecycle
    // ------------------------------------------------------------------

    private void BeginSequence(int sequenceId)
    {
        if (!sequenceRegistry.TryGetValue(sequenceId, out DialogueSequenceSO sequence))
        {
            Debug.LogWarning($"DialogueUIManager: Sequence ID {sequenceId} not registered. " +
                             "Add the SO to the sequenceAssets list on the DialogueUIManager.");
            entityManager.SetComponentEnabled<ActiveDialogue>(managerEntity, false);
            return;
        }

        activeSequenceId = sequenceId;
        currentSequence  = sequence;
        awaitingChoice   = false;

        BuildLookupTables(sequence);

        // Find the Start node and follow its "out" connection.
        string startNodeId = FindStartNodeId();
        if (startNodeId == null)
        {
            Debug.LogError($"DialogueUIManager: Sequence '{sequence.name}' has no Start node. " +
                           "Open it in the Dialogue Editor and add a Start node.");
            EndSequence(markAsPlayed: false);
            return;
        }

        dialoguePanel.SetActive(true);
        AdvanceFromCurrentNode_FromNode(startNodeId, "out");
    }

    /// <summary>
    /// Builds nodeById and connectionMap from the loaded sequence SO.
    /// Called once at the start of each new sequence.
    /// </summary>
    private void BuildLookupTables(DialogueSequenceSO sequence)
    {
        nodeById.Clear();
        connectionMap.Clear();

        foreach (DialogueNodeData node in sequence.nodes)
        {
            if (node != null)
                nodeById[node.nodeId] = node;
        }

        foreach (DialogueConnectionData connection in sequence.connections)
        {
            connectionMap[(connection.fromNodeId, connection.fromPortId)] = connection.toNodeId;
        }
    }

    private string FindStartNodeId()
    {
        foreach (DialogueNodeData node in nodeById.Values)
        {
            if (node is DialogueStartNodeData)
                return node.nodeId;
        }
        return null;
    }

    /// <summary>
    /// Follows the output port named portId from currentNodeId and displays the next node.
    /// </summary>
    private void AdvanceFromCurrentNode(string portId)
    {
        AdvanceFromCurrentNode_FromNode(currentNodeId, portId);
    }

    private void AdvanceFromCurrentNode_FromNode(string fromNodeId, string portId)
    {
        if (!connectionMap.TryGetValue((fromNodeId, portId), out string nextNodeId))
        {
            // No connection from this port — treat as end of sequence.
            EndSequence(markAsPlayed: true);
            return;
        }

        currentNodeId = nextNodeId;
        DisplayNode(nextNodeId);
    }

    private void EndSequence(bool markAsPlayed)
    {
        if (markAsPlayed && currentSequence != null)
        {
            // Only mark the primary sequence (not refreshers) as played.
            ActiveDialogue activeDialogue = entityManager.GetComponentData<ActiveDialogue>(managerEntity);
            bool isPrimary = true;
            if (entityManager.HasComponent<DialogueProvider>(activeDialogue.speakerEntity))
            {
                DialogueProvider provider = entityManager.GetComponentData<DialogueProvider>(activeDialogue.speakerEntity);
                isPrimary = activeDialogue.sequenceId == provider.sequenceId;
            }

            if (isPrimary)
            {
                DynamicBuffer<PlayedDialogue> playedBuffer = entityManager.GetBuffer<PlayedDialogue>(gameDataEntity);
                bool alreadyMarked = false;
                for (int bufferIndex = 0; bufferIndex < playedBuffer.Length; bufferIndex++)
                {
                    if (playedBuffer[bufferIndex].sequenceId == currentSequence.sequenceId)
                    {
                        alreadyMarked = true;
                        break;
                    }
                }
                if (!alreadyMarked)
                    playedBuffer.Add(new PlayedDialogue { sequenceId = currentSequence.sequenceId });
            }
        }

        entityManager.SetComponentEnabled<ActiveDialogue>(managerEntity, false);
        CloseDialoguePanel();
    }

    private void CloseDialoguePanel()
    {
        activeSequenceId = -1;
        currentSequence  = null;
        currentNodeId    = null;
        awaitingChoice   = false;
        pendingBranches.Clear();

        dialoguePanel.SetActive(false);
        choiceGroup.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Node display
    // ------------------------------------------------------------------

    private void DisplayNode(string nodeId)
    {
        if (!nodeById.TryGetValue(nodeId, out DialogueNodeData node))
        {
            Debug.LogError($"DialogueUIManager: Node ID '{nodeId}' not found in current sequence.");
            EndSequence(markAsPlayed: true);
            return;
        }

        switch (node)
        {
            case DialogueLineNodeData lineNode:
                DisplayLineNode(lineNode);
                break;

            case DialogueDecisionNodeData decisionNode:
                DisplayDecisionNode(decisionNode);
                break;

            case DialogueEventNodeData eventNode:
                DisplayEventNode(eventNode);
                break;

            case DialogueEndNodeData _:
                EndSequence(markAsPlayed: true);
                break;

            case DialogueStartNodeData _:
                // Start node should never be reached mid-sequence.
                Debug.LogWarning("DialogueUIManager: Execution reached a Start node mid-sequence. Check your connections.");
                EndSequence(markAsPlayed: true);
                break;

            default:
                Debug.LogWarning($"DialogueUIManager: Unknown node type '{node.GetType().Name}'.");
                EndSequence(markAsPlayed: true);
                break;
        }
    }

    /// <summary>
    /// Shows the subtitle text and speaker name. Waits for the player to press Interact
    /// before following the "out" connection to the next node.
    /// </summary>
    private void DisplayLineNode(DialogueLineNodeData lineNode)
    {
        choiceGroup.SetActive(false);
        awaitingChoice = false;

        subtitleLabel.text = lineNode.dialogueText;

        bool hasSpeaker = !string.IsNullOrEmpty(lineNode.speakerName);
        speakerNameLabel.gameObject.SetActive(hasSpeaker);
        if (hasSpeaker)
            speakerNameLabel.text = lineNode.speakerName;
    }

    /// <summary>
    /// Shows one choice button per branch. The player taps a button to follow that branch's
    /// output port to the next node. Branches beyond the wired button count are skipped.
    /// </summary>
    private void DisplayDecisionNode(DialogueDecisionNodeData decisionNode)
    {
        awaitingChoice   = true;
        pendingBranches  = decisionNode.branches;

        subtitleLabel.text = string.Empty;
        speakerNameLabel.gameObject.SetActive(false);
        choiceGroup.SetActive(true);

        int branchCount = decisionNode.branches.Count;

        for (int buttonIndex = 0; buttonIndex < choiceButtons.Length; buttonIndex++)
        {
            if (buttonIndex >= branchCount)
            {
                choiceButtons[buttonIndex].gameObject.SetActive(false);
                continue;
            }

            choiceButtonLabels[buttonIndex].text = decisionNode.branches[buttonIndex].choiceText;
            choiceButtons[buttonIndex].gameObject.SetActive(true);
        }

        // If there are no branches at all, skip the decision and advance.
        if (branchCount == 0)
        {
            awaitingChoice = false;
            choiceGroup.SetActive(false);
            EndSequence(markAsPlayed: true);
        }
    }

    /// <summary>
    /// Fires the event ID (if any) and immediately follows "out" without waiting for input.
    /// The player does not see this node.
    /// </summary>
    private void DisplayEventNode(DialogueEventNodeData eventNode)
    {
        if (eventNode.eventId != DialogueIds.Events.None)
        {
            entityManager.SetComponentData<OnDialogueEvent>(managerEntity,
                new OnDialogueEvent { eventId = eventNode.eventId });
            entityManager.SetComponentEnabled<OnDialogueEvent>(managerEntity, true);
        }

        // Continue immediately — no player input needed.
        AdvanceFromCurrentNode("out");
    }

    // ------------------------------------------------------------------
    // Choice selection
    // ------------------------------------------------------------------

    private void OnChoiceSelected(int buttonIndex)
    {
        if (!awaitingChoice) return;
        if (pendingBranches == null || buttonIndex >= pendingBranches.Count) return;

        awaitingChoice = false;
        choiceGroup.SetActive(false);

        string chosenPortId = pendingBranches[buttonIndex].portId;
        AdvanceFromCurrentNode(chosenPortId);
    }

    // ------------------------------------------------------------------
    // Input helper
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns true if OnInteractPlayerInput is enabled on the player entity this frame,
    /// and disables it (consuming the input event) before returning.
    ///
    /// ECS simulation runs before MonoBehaviour Update() in the Unity player loop.
    /// DialogueStartSystem consumes this event on the frame that starts dialogue,
    /// so this method only sees it on subsequent "advance" frames.
    /// </summary>
    private bool ConsumeInteractInput()
    {
        if (!entityManager.IsComponentEnabled<OnInteractPlayerInput>(playerEntity)) return false;
        entityManager.SetComponentEnabled<OnInteractPlayerInput>(playerEntity, false);
        return true;
    }
}
