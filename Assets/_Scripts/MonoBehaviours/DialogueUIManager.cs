using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

// Walks the DialogueSequenceSO node graph that DialogueStartSystem enables via ActiveDialogue.
// A sequence is marked played only when it ends through an End node; a played sequence starts at its Refresher node if one exists.
// Decision branches beyond the wired choice button count are silently skipped.
public class DialogueUIManager : MonoBehaviour
{
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

    private EntityManager entityManager;
    private Entity managerEntity;
    private Entity playerEntity;
    private Entity gameDataEntity;

    private Dictionary<int, DialogueSequenceSO> sequenceRegistry = new Dictionary<int, DialogueSequenceSO>();

    private Dictionary<string, DialogueNodeData> nodeById = new Dictionary<string, DialogueNodeData>();

    private Dictionary<(string nodeId, string portId), string> connectionMap =
        new Dictionary<(string, string), string>();

    private DialogueSequenceSO currentSequence;
    private string currentNodeId;
    private bool awaitingChoice;
    private int activeSequenceId = -1;  // -1 means no dialogue is displayed

    private const float UnresolvedGraceSeconds = 5f;
    private float unresolvedSeconds;
    private bool hasReportedUnresolvedEntities;

    private List<DialogueDecisionBranch> pendingBranches = new List<DialogueDecisionBranch>();

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

    // Called from Update until it succeeds, never once from Start: SubScene entities stream in after Start,
    // so a one-shot resolve would leave the manager inert all session.
    private bool TryResolveEcsReferences()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;

        entityManager = world.EntityManager;

        EntityQuery managerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<DialogueManagerTag>());
        EntityQuery playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
        EntityQuery gameDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<GameDataTag>());
        if (managerQuery.IsEmpty || playerQuery.IsEmpty || gameDataQuery.IsEmpty) return false;

        managerEntity  = managerQuery.GetSingletonEntity();
        playerEntity   = playerQuery.GetSingletonEntity();
        gameDataEntity = gameDataQuery.GetSingletonEntity();
        return true;
    }

    private void Update()
    {
        if (managerEntity == Entity.Null && !TryResolveEcsReferences())
        {
            ReportUnresolvedEntitiesOnce();
            return;
        }

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

        bool alreadyPlayed = HasBeenPlayed(sequenceId);
        string refresherNodeId = alreadyPlayed ? FindRefresherNodeId() : null;
        string entryNodeId     = refresherNodeId ?? FindStartNodeId();

        if (entryNodeId == null)
        {
            Debug.LogError($"DialogueUIManager: Sequence '{sequence.name}' has no Start node. " +
                           "Open it in the Dialogue Editor and add a Start node.");
            EndSequence(markAsPlayed: false);
            return;
        }

        dialoguePanel.SetActive(true);
        AdvanceFromCurrentNode_FromNode(entryNodeId, "out");
    }

    private bool HasBeenPlayed(int sequenceId)
    {
        DynamicBuffer<PlayedDialogue> playedDialogues = entityManager.GetBuffer<PlayedDialogue>(gameDataEntity);
        for (int bufferIndex = 0; bufferIndex < playedDialogues.Length; bufferIndex++)
        {
            if (playedDialogues[bufferIndex].sequenceId == sequenceId)
                return true;
        }
        return false;
    }

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

    private string FindRefresherNodeId()
    {
        foreach (DialogueNodeData node in nodeById.Values)
        {
            if (node is DialogueRefresherNodeData)
                return node.nodeId;
        }
        return null;
    }

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
        if (markAsPlayed && currentSequence != null && !HasBeenPlayed(currentSequence.sequenceId))
        {
            DynamicBuffer<PlayedDialogue> playedBuffer = entityManager.GetBuffer<PlayedDialogue>(gameDataEntity);
            playedBuffer.Add(new PlayedDialogue { sequenceId = currentSequence.sequenceId });
        }

        entityManager.SetComponentEnabled<ActiveDialogue>(managerEntity, false);
        CloseDialoguePanel();
    }

    // A scene missing these entities looks identical to a SubScene still streaming in, so the error waits out a grace period.
    private void ReportUnresolvedEntitiesOnce()
    {
        if (hasReportedUnresolvedEntities) return;

        unresolvedSeconds += Time.unscaledDeltaTime;
        if (unresolvedSeconds < UnresolvedGraceSeconds) return;

        hasReportedUnresolvedEntities = true;
        Debug.LogError(
            "DialogueUIManager: still no DialogueManagerAuthoring / Player / GameDataTag entity after "
            + UnresolvedGraceSeconds + "s. Dialogue will not display in this scene.");
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
            case DialogueRefresherNodeData _:
                // Entry nodes should never be reached mid-sequence.
                Debug.LogWarning($"DialogueUIManager: Execution reached a {node.GetType().Name} node mid-sequence. Check your connections.");
                EndSequence(markAsPlayed: true);
                break;

            default:
                Debug.LogWarning($"DialogueUIManager: Unknown node type '{node.GetType().Name}'.");
                EndSequence(markAsPlayed: true);
                break;
        }
    }

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

    private void DisplayEventNode(DialogueEventNodeData eventNode)
    {
        if (eventNode.eventId != DialogueIds.Events.None)
        {
            entityManager.SetComponentData<OnDialogueEvent>(managerEntity,
                new OnDialogueEvent { eventId = eventNode.eventId });
            entityManager.SetComponentEnabled<OnDialogueEvent>(managerEntity, true);
        }

        AdvanceFromCurrentNode("out");
    }

    private void OnChoiceSelected(int buttonIndex)
    {
        if (!awaitingChoice) return;
        if (pendingBranches == null || buttonIndex >= pendingBranches.Count) return;

        awaitingChoice = false;
        choiceGroup.SetActive(false);

        string chosenPortId = pendingBranches[buttonIndex].portId;
        AdvanceFromCurrentNode(chosenPortId);
    }

    // ECS runs before MonoBehaviour Update; DialogueStartSystem consumes the interact event on the starting frame,
    // so this only sees later "advance" presses.
    private bool ConsumeInteractInput()
    {
        if (!entityManager.IsComponentEnabled<OnInteractPlayerInput>(playerEntity)) return false;
        entityManager.SetComponentEnabled<OnInteractPlayerInput>(playerEntity, false);
        return true;
    }
}
