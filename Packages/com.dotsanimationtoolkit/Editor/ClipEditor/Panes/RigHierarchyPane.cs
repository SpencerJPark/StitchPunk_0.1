// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    // What a hierarchy row stands for: the rig's parts, and the previewed prefab's transforms —
    // two kinds sharing one tree since they are the two kinds of thing a clip animates. An enum
    // rather than a pair of booleans, since most code asks "is this a prefab transform".
    internal enum HierarchyItemKind
    {
        /// <summary>A transform of the previewed prefab.</summary>
        PrefabTransform,

        /// <summary>A part the rig declares, which transform and flipbook tracks bind to.</summary>
        RigTarget
    }

    internal sealed class HierarchyItem
    {
        public HierarchyItemKind kind;
        public string displayName;

        // The rig part this row is, or 0 when the rig declares none for it. Set for a rig-target
        // row always, and for a previewed node whenever a part records that node's path as its
        // source — everything asking "which part is this row" reads this, not the row's kind.
        public uint targetId;

        /// <summary>Set for a previewed transform: its index in the preview's hierarchy.</summary>
        public int previewIndex;
    }

    /// <summary>The Hierarchy pane: the Rig field, the tree of rig parts and prefab transforms, drag-to-reparent, and the selection every other surface reads; it reaches the window only through its events and the session.</summary>
    public sealed class RigHierarchyPane : VisualElement, System.IDisposable
    {
        private const string HiddenUssClassName = "clip-editor--hidden";
        private const string HierarchyRowUssClassName = "clip-editor__hierarchy-row";
        private const string AnimatedBoneUssClassName = "clip-editor__hierarchy-row--animated";
        private const string BillboardRootUssClassName = "clip-editor__hierarchy-row--billboard-root";
        private const string BillboardInheritedUssClassName =
            "clip-editor__hierarchy-row--billboard-inherited";

        /// <summary>
        /// Marks a declared billboard root in the tree. A glyph rather than a texture: the row is a
        /// <c>Label</c>, so a prefix costs no layout change, survives every theme, and cannot go
        /// missing the way a packaged icon can.
        /// </summary>
        private const string BillboardRootGlyph = "◈ ";

        /// <summary>Marks a node inheriting a billboard from an ancestor. Deliberately fainter.</summary>
        private const string BillboardInheritedGlyph = "· ";

        private TreeView hierarchyTreeView;
        private Label hierarchyEmptyLabel;

        private ObjectField skinnedSourceField;

        // What is selected in the hierarchy, as the tree item id — which is also the preview's
        // index for the same transform. -1 is nothing. An index rather than a name, since names
        // repeat; an index rather than a Transform, since the preview skeleton rebuilds whenever the rig changes.
        private int selectedHierarchyItemId = NothingSelectedItemId;

        /// <summary>
        /// Tree ids for rig targets, which must not collide with the preview hierarchy indices that
        /// id the transform rows. Preview indices are always ≥ 0, so targets take the negatives —
        /// no threshold constant to outgrow, unlike an offset scheme.
        /// </summary>
        private const int RigTargetItemIdBase = -2;

        /// <summary>
        /// Not −1: that is a legitimate tree id under <see cref="RigTargetItemIdBase"/>'s scheme,
        /// and overloading it would make the first rig target indistinguishable from no selection.
        /// </summary>
        internal const int NothingSelectedItemId = int.MinValue;

        private readonly Dictionary<int, HierarchyItem> hierarchyItemsById =
            new Dictionary<int, HierarchyItem>();

        /// <summary>
        /// Every selected row, in tree order. The timeline shows the tracks of all of them and the
        /// inspector gives each its own labelled block.
        /// </summary>
        private readonly List<HierarchyItem> selectedHierarchyItems = new List<HierarchyItem>();

        /// <summary>The row the gizmo and the viewport outline follow, of the several selected.</summary>
        private int activeHierarchyItemId = NothingSelectedItemId;

        /// <summary>
        /// The previous selection, so the next change can be diffed to find the row just added.
        /// </summary>
        private readonly HashSet<int> previouslySelectedItemIds = new HashSet<int>();

        /// <summary>Set while a selection change is being applied, to stop it re-entering itself.</summary>
        private bool isHandlingHierarchySelection;

        private Button editPrefabButton;

        // The selected transform's name, which is the identity bone tracks bind by. Carried
        // alongside the index rather than derived from it, so the bake's contract and the window's
        // selection stay separate things.
        private string selectedBoneName;

        /// <summary>The selected rig target, or 0 when the selection is a bone or nothing.</summary>
        private uint selectedTargetId;

        /// <summary>The selected socket, or 0 when the selection is anything else.</summary>
        private uint selectedSocketId;

        private ActiveAssetSelection selection;
        private ClipEditorSession session;
        private ClipPreviewController previewController;

        // Window facts the pane needs but does not own, handed in once at bind time.
        internal System.Func<bool> IsRigEditMode { get; set; }
        internal System.Func<uint, string> ResolveTargetDisplayName { get; set; }

        // Raised after the tree's own selection has been applied; the window clears the key selection
        // and rebuilds the timeline and the inspector.
        internal event System.Action TreeSelectionChanged;
        internal event System.Action SelectionCleared;
        internal event System.Action<ContextualMenuPopulateEvent, HierarchyItem> ContextMenuRequested;
        internal event System.Action<HierarchyItem> PrefabOpenRequested;
        internal event System.Action<HierarchyItem, HierarchyItem> ReparentRequested;

        internal List<HierarchyItem> SelectedHierarchyItems { get { return selectedHierarchyItems; } }
        internal uint SelectedTargetId { get { return selectedTargetId; } }
        internal string SelectedBoneName { get { return selectedBoneName; } }

        // Settable: focusing a socket from the component stack points the gizmo at it without a row change.
        internal uint SelectedSocketId
        {
            get { return selectedSocketId; }
            set { selectedSocketId = value; }
        }

        public void Bind(
            VisualElement paneRoot,
            ActiveAssetSelection sharedSelection,
            ClipEditorSession editorSession,
            ClipPreviewController preview)
        {
            selection = sharedSelection;
            session = editorSession;
            previewController = preview;

            if (paneRoot != null)
            {
                // The rig this window plays the open set against. Window state only — no asset records
                // it, and picking one here changes nothing any actor bakes. The rig's own sourcePrefab
                // is what the preview instantiates and the hierarchy pane lists, so an empty field is
                // an empty hierarchy pane rather than a missing one.
                skinnedSourceField = paneRoot.Q<ObjectField>("skinned-source-field");
                if (skinnedSourceField != null)
                {
                    skinnedSourceField.objectType = typeof(RigAsset);
                    skinnedSourceField.allowSceneObjects = false;
                    skinnedSourceField.tooltip =
                        "The rig this window animates. Its Source Prefab is what the hierarchy lists "
                        + "and the preview instantiates. Shared with every tab — the Rigs tab picks it "
                        + "too.";
                    skinnedSourceField.RegisterValueChangedCallback(OnSkinnedSourceFieldChanged);
                }

                BindHierarchy(paneRoot);
                selection.RigChanged += OnRigChanged;
            }
        }

        public void Dispose()
        {
            if (selection != null)
            {
                selection.RigChanged -= OnRigChanged;
            }
            if (skinnedSourceField != null)
            {
                skinnedSourceField.UnregisterValueChangedCallback(OnSkinnedSourceFieldChanged);
            }
            if (hierarchyTreeView != null)
            {
                hierarchyTreeView.selectionChanged -= OnHierarchySelectionChanged;
            }
            if (editPrefabButton != null)
            {
                editPrefabButton.clicked -= OnEditPrefabClicked;
            }
        }

        // Through the tree's own selection, so it notifies like a click would.
        internal void SelectItemById(int itemId)
        {
            if (hierarchyTreeView == null)
            {
                return;
            }
            hierarchyTreeView.SetSelectionById(itemId);
            hierarchyTreeView.ScrollToItemById(itemId);
        }

        internal void SelectItemsById(List<int> itemIds)
        {
            if (hierarchyTreeView == null)
            {
                return;
            }
            hierarchyTreeView.SetSelectionById(itemIds);
        }

        internal void SelectItemByIdWithoutNotify(int itemId)
        {
            if (hierarchyTreeView == null)
            {
                return;
            }
            hierarchyTreeView.SetSelectionByIdWithoutNotify(new int[] { itemId });
            hierarchyTreeView.ScrollToItemById(itemId);
        }

        internal void ClearTreeSelectionWithoutNotify()
        {
            if (hierarchyTreeView == null)
            {
                return;
            }
            hierarchyTreeView.SetSelectionWithoutNotify(new int[0]);
        }

        private void OnSkinnedSourceFieldChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            selection.SetRig(changeEvent.newValue as RigAsset);
        }

        private void OnRigChanged(RigAsset rig)
        {
            skinnedSourceField?.SetValueWithoutNotify(rig);
        }

        private void OnEditPrefabClicked()
        {
            PrefabOpenRequested?.Invoke(ActiveHierarchyItem);
        }

        private RigAsset ActiveRig
        {
            get { return selection != null ? selection.Rig : null; }
        }

        // The one place that reads the rig's prefab, so every consumer below follows the rig field
        // to the same answer.
        private GameObject LoadedPrefab
        {
            get { return ActiveRig != null ? ActiveRig.sourcePrefab : null; }
        }

        private void BindHierarchy(VisualElement paneRoot)
        {
            hierarchyEmptyLabel = paneRoot.Q<Label>("hierarchy-empty-label");

            hierarchyTreeView = paneRoot.Q<TreeView>("hierarchy-tree");
            if (hierarchyTreeView == null)
            {
                return;
            }
            hierarchyTreeView.fixedItemHeight = 20f;

            // Multiple, so several parts can be focused on the timeline at once. Ctrl-click adds,
            // shift-click extends — the conventions every list in the editor already uses.
            hierarchyTreeView.selectionType = SelectionType.Multiple;
            hierarchyTreeView.makeItem = MakeHierarchyRow;
            hierarchyTreeView.bindItem = BindHierarchyRow;
            hierarchyTreeView.selectionChanged += OnHierarchySelectionChanged;

            editPrefabButton = paneRoot.Q<Button>("edit-prefab-button");
            if (editPrefabButton != null)
            {
                editPrefabButton.clicked += OnEditPrefabClicked;
            }
            RefreshPrefabActionState();
        }

        // Disabled rather than left to fail on click: a scene object dropped into the rig field
        // has no prefab asset behind it to open.
        internal void RefreshPrefabActionState()
        {
            if (editPrefabButton == null)
            {
                return;
            }
            bool canOpen = PrefabAuthoringBridge.CanOpen(LoadedPrefab);
            editPrefabButton.SetEnabled(canOpen);
            editPrefabButton.tooltip = canOpen
                ? "Open this prefab in Unity's prefab mode. Structural edits — parenting, adding "
                    + "parts, moving meshes — belong there, not here."
                : "Pick a rig above the hierarchy, and give that rig a Source Prefab, to edit it.";
        }

        /// <summary>The path of a hierarchy row's object below the prefab root, for addressing it in a stage.</summary>
        internal string ResolveHierarchyPath(HierarchyItem item)
        {
            if (item == null || previewController == null)
            {
                return string.Empty;
            }

            Transform root = previewController.HierarchyRoot;
            if (root == null)
            {
                return string.Empty;
            }

            Transform node;
            switch (item.kind)
            {
                case HierarchyItemKind.RigTarget:
                    // The recorded path first, when the target has one: two planes called "Plane"
                    // is the ordinary case, and a name match would pick whichever came first.
                    node = ResolveTargetSourceNode(item.targetId, root);
                    if (node == null)
                    {
                        node = PrefabAuthoringBridge.FindByName(root, item.displayName);
                    }
                    break;
                default:
                    node = previewController.GetTransformByIndex(item.previewIndex);
                    break;
            }
            return node != null ? PrefabAuthoringBridge.GetHierarchyPath(node, root) : string.Empty;
        }

        /// <summary>The previewed node a rig target records as its source, or null when it has none.</summary>
        internal Transform ResolveTargetSourceNode(uint targetId, Transform root)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.targets == null || root == null || targetId == 0u)
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null || target.Id.Value != targetId
                    || string.IsNullOrEmpty(target.sourceNodePath))
                {
                    continue;
                }
                return PrefabAuthoringBridge.ResolveByPath(root, target.sourceNodePath);
            }
            return null;
        }

        internal int FindItemIdByName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return NothingSelectedItemId;
            }
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value != null && pair.Value.displayName == displayName)
                {
                    return pair.Key;
                }
            }
            return NothingSelectedItemId;
        }

        // -------------------------------------------------------------------------------------
        // Prefab hierarchy. The rig's transforms, as the pick list for bone tracks.
        // -------------------------------------------------------------------------------------

        /// <summary>Rebuilds the hierarchy from the assigned rigged prefab.</summary>
        internal void RebuildHierarchy()
        {
            if (hierarchyTreeView == null)
            {
                return;
            }

            hierarchyItemsById.Clear();
            List<TreeViewItemData<HierarchyItem>> rootItems = new List<TreeViewItemData<HierarchyItem>>();

            // The rig's parts come first: they are what a cutout clip animates, and they are what a
            // flipbook track binds to. Before this they appeared nowhere in the window, so a
            // flipbook track had no object to belong to.
            rootItems.AddRange(BuildRigTargetItems());

            // Built from the preview's live instance, not from the prefab asset. The viewport picks
            // transforms out of that instance, so sourcing the tree from it means a picked object is
            // literally a node of the tree's own source — no mapping between two hierarchies that
            // have to be kept in agreement.
            Transform hierarchyRoot = previewController != null ? previewController.HierarchyRoot : null;
            if (hierarchyRoot != null)
            {
                rootItems.Add(BuildHierarchyItem(hierarchyRoot));
            }

            hierarchyTreeView.SetRootItems(rootItems);
            hierarchyTreeView.Rebuild();
            if (rootItems.Count > 0)
            {
                // Expanded up front so any id the viewport picks has a visible row to select and
                // scroll to, without the window having to walk up and expand ancestors first.
                hierarchyTreeView.ExpandAll();
            }

            if (hierarchyEmptyLabel != null)
            {
                hierarchyEmptyLabel.text = ResolveHierarchyEmptyMessage();
                hierarchyEmptyLabel.EnableInClassList(HiddenUssClassName, rootItems.Count > 0);
            }
        }

        /// <summary>What the empty-hierarchy hint should say, given why it is empty.</summary>
        private string ResolveHierarchyEmptyMessage()
        {
            if (selection.ClipSet == null)
            {
                return "Assign a clip set.";
            }
            if (ActiveRig == null)
            {
                return "Pick a rig above the hierarchy.";
            }
            if (ActiveRig.sourcePrefab == null)
            {
                return "Rig \"" + ActiveRig.name + "\" has no Source Prefab assigned yet. Open "
                    + "the rig asset and assign one to preview and author bone tracks.";
            }
            return "This rig's source prefab has no child transforms to show.";
        }

        // The id is the preview's own index for that transform, not a counter kept here — two
        // independent walks would agree only as long as nobody changed one of them.
        /// <summary>Builds one tree item, taking its id from the preview.</summary>
        private TreeViewItemData<HierarchyItem> BuildHierarchyItem(Transform transformNode)
        {
            List<TreeViewItemData<HierarchyItem>> childItems =
                new List<TreeViewItemData<HierarchyItem>>();
            for (int childIndex = 0; childIndex < transformNode.childCount; childIndex++)
            {
                childItems.Add(BuildHierarchyItem(transformNode.GetChild(childIndex)));
            }

            int itemId = previewController.GetHierarchyIndex(transformNode);
            HierarchyItem item = new HierarchyItem
            {
                kind = HierarchyItemKind.PrefabTransform,
                displayName = transformNode.name,
                previewIndex = itemId,
                targetId = ResolveNodeTargetId(transformNode)
            };
            hierarchyItemsById[itemId] = item;
            return new TreeViewItemData<HierarchyItem>(itemId, item, childItems);
        }

        /// <summary>The rig part claiming a previewed node, or 0 when none does.</summary>
        private uint ResolveNodeTargetId(Transform transformNode)
        {
            RigAsset rig = ActiveRig;
            Transform root = previewController != null ? previewController.HierarchyRoot : null;
            if (rig == null || root == null || transformNode == null)
            {
                return 0u;
            }
            return ClipComponentModel.ResolveTargetIdForNode(
                rig, PrefabAuthoringBridge.GetHierarchyPath(transformNode, root));
        }

        // A target recording which previewed node it stands for is skipped: that node's own row is
        // where it appears, and two rows for one part could not be told apart.
        /// <summary>One row per rig target that has no node of its own, flat.</summary>
        private List<TreeViewItemData<HierarchyItem>> BuildRigTargetItems()
        {
            List<TreeViewItemData<HierarchyItem>> targetItems =
                new List<TreeViewItemData<HierarchyItem>>();
            RigAsset rig = ActiveRig;
            if (rig == null || rig.targets == null)
            {
                return targetItems;
            }

            Transform hierarchyRoot = previewController != null
                ? previewController.HierarchyRoot
                : null;

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }
                if (hierarchyRoot != null && !string.IsNullOrEmpty(target.sourceNodePath)
                    && PrefabAuthoringBridge.ResolveByPath(
                        hierarchyRoot, target.sourceNodePath) != null)
                {
                    continue;
                }

                int itemId = RigTargetItemIdBase - targetIndex;
                HierarchyItem item = new HierarchyItem
                {
                    kind = HierarchyItemKind.RigTarget,
                    displayName = string.IsNullOrEmpty(target.displayName)
                        ? "Target " + target.Id.Value.ToString()
                        : target.displayName,
                    targetId = target.Id.Value
                };
                hierarchyItemsById[itemId] = item;
                targetItems.Add(new TreeViewItemData<HierarchyItem>(itemId, item));
            }
            return targetItems;
        }

        /// <summary>A socket's one-line label: its name, what it follows, and a mark when that resolves to nothing.</summary>
        internal string DescribeSocketLabel(SocketDefinition socket)
        {
            string name = string.IsNullOrEmpty(socket.displayName)
                ? "Socket " + socket.Id.Value.ToString()
                : socket.displayName;

            string follows = socket.mode == SocketAttachMode.RigTarget
                ? ResolveTargetDisplayName(socket.targetId)
                : (string.IsNullOrEmpty(socket.boneName) ? "<no bone>" : socket.boneName);

            bool resolved = previewController != null && previewController.IsSocketResolved(socket);
            return name + "  →  " + follows + (resolved ? string.Empty : "   (unresolved)");
        }

        /// <summary>The socket with this id on the loaded rig, or null.</summary>
        internal SocketDefinition FindSocket(uint socketId)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.sockets == null)
            {
                return null;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket != null && socket.Id.Value == socketId)
                {
                    return socket;
                }
            }
            return null;
        }

        internal int FindSocketIndex(uint socketId)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.sockets == null)
            {
                return -1;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket != null && socket.Id.Value == socketId)
                {
                    return socketIndex;
                }
            }
            return -1;
        }

        // Manipulator and double-click callback are attached once, reading the row's current item
        // through a field the bind step refreshes — rows are recycled, so binding per-item would
        // stack a new handler on the same element every time it scrolled back into view.
        /// <summary>One hierarchy row, wired for the two gestures that reach prefab mode.</summary>
        private VisualElement MakeHierarchyRow()
        {
            HierarchyRowLabel label = new HierarchyRowLabel();
            label.AddToClassList(HierarchyRowUssClassName);

            label.AddManipulator(new ContextualMenuManipulator(
                menuEvent => ContextMenuRequested?.Invoke(menuEvent, label.item)));

            label.RegisterCallback<PointerDownEvent>(pointerEvent =>
            {
                if (pointerEvent.clickCount >= 2 && pointerEvent.button == 0)
                {
                    PrefabOpenRequested?.Invoke(label.item);
                }
            });

            RegisterReparentDrag(label);

            return label;
        }

        // Built on DragAndDrop and UI Toolkit's drag events, not the TreeView's own drag hooks
        // (not public in this Unity version) — the built-in reorderable flag would reorder the
        // view and leave the prefab untouched, which is the parallel hierarchy this must not become.
        /// <summary>Wires one row for drag-to-reparent, which only does anything in Rig Edit mode.</summary>
        private void RegisterReparentDrag(HierarchyRowLabel label)
        {
            label.RegisterCallback<PointerMoveEvent>(pointerEvent =>
            {
                if (!IsRigEditMode()
                    || pointerEvent.pressedButtons != 1
                    || label.item == null
                    || label.item.kind != HierarchyItemKind.PrefabTransform)
                {
                    return;
                }

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(ReparentDragKey, label.item);
                DragAndDrop.objectReferences = new Object[0];
                DragAndDrop.StartDrag("Reparent " + label.item.displayName);
                pointerEvent.StopPropagation();
            });

            label.RegisterCallback<DragUpdatedEvent>(dragEvent =>
            {
                DragAndDrop.visualMode = CanDropOn(label.item)
                    ? DragAndDropVisualMode.Move
                    : DragAndDropVisualMode.Rejected;
                dragEvent.StopPropagation();
            });

            label.RegisterCallback<DragPerformEvent>(dragEvent =>
            {
                HierarchyItem dragged = DragAndDrop.GetGenericData(ReparentDragKey) as HierarchyItem;
                if (CanDropOn(label.item) && dragged != null)
                {
                    DragAndDrop.AcceptDrag();
                    ReparentRequested?.Invoke(dragged, label.item);
                }
                dragEvent.StopPropagation();
            });
        }

        private const string ReparentDragKey = "DotsAnimationToolkit.ReparentItem";

        /// <summary>Whether the row under the cursor is a legal drop target for the current drag.</summary>
        private bool CanDropOn(HierarchyItem dropTarget)
        {
            if (!IsRigEditMode() || dropTarget == null
                || dropTarget.kind != HierarchyItemKind.PrefabTransform)
            {
                return false;
            }

            HierarchyItem dragged = DragAndDrop.GetGenericData(ReparentDragKey) as HierarchyItem;
            if (dragged == null || dragged == dropTarget
                || dragged.kind != HierarchyItemKind.PrefabTransform)
            {
                return false;
            }

            // The deep check, asked of the preview's copy of the hierarchy. It answers the same
            // question the write would ask of the asset, so an illegal drop is a rejected cursor
            // rather than a notification after the fact.
            if (previewController == null)
            {
                return false;
            }
            Transform draggedNode = previewController.GetTransformByIndex(dragged.previewIndex);
            Transform targetNode = previewController.GetTransformByIndex(dropTarget.previewIndex);
            string ignoredError;
            return RigStructureEditor.ValidateReparent(draggedNode, targetNode, out ignoredError);
        }

        /// <summary>A row label that remembers which item it is currently showing.</summary>
        private sealed class HierarchyRowLabel : Label
        {
            public HierarchyItem item;
        }

        private void BindHierarchyRow(VisualElement element, int index)
        {
            HierarchyRowLabel label = element as HierarchyRowLabel;
            if (label == null)
            {
                return;
            }
            HierarchyItem item = hierarchyTreeView.GetItemDataForIndex<HierarchyItem>(index);
            label.item = item;
            if (item == null)
            {
                label.text = string.Empty;
                return;
            }
            label.text = item.displayName;

            // Bold marks something the selected clip already animates, so the tree doubles as the
            // answer to "what does this clip actually touch?".
            // Either binding counts. A claimed node can be animated as a part and still carry a
            // bone track left over from before it was one, and a row that went un-bolded because
            // the wrong half was checked would say this clip does not touch it.
            bool isAnimated = item.targetId != 0u && CountTracksForTarget(item.targetId) > 0;
            if (!isAnimated && item.kind != HierarchyItemKind.RigTarget)
            {
                isAnimated = FindBoneTrackIndex(item.displayName) >= 0;
            }
            label.EnableInClassList(AnimatedBoneUssClassName, isAnimated);
            ApplyBillboardIndicator(label, item);
        }

        /// <summary>The rig target with this id, or null.</summary>
        internal RigTargetDefinition FindRigTargetById(uint targetId)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.targets == null)
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null && target.Id.Value == targetId)
                {
                    return target;
                }
            }
            return null;
        }

        // Three states, not two: "this node billboards" and "this node decides how it billboards"
        // are different facts, and a fully billboarded node's animated rotation is replaced outright
        // at resolve time, so keying it changes nothing visible without a marker to explain why.
        /// <summary>Marks a row as a billboard root, as inheriting one, or as neither.</summary>
        private void ApplyBillboardIndicator(HierarchyRowLabel label, HierarchyItem item)
        {
            label.EnableInClassList(BillboardRootUssClassName, false);
            label.EnableInClassList(BillboardInheritedUssClassName, false);
            label.tooltip = string.Empty;

            RigAsset rig = ActiveRig;
            if (rig == null || rig.billboardRoots == null || rig.billboardRoots.Count == 0)
            {
                return;
            }

            Transform node = ResolveHierarchyTransform(item);
            if (node == null)
            {
                return;
            }

            Transform previewRoot = previewController != null ? previewController.HierarchyRoot : null;
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, previewRoot, null);
            int rootIndex =
                BillboardRootResolver.FindNearestRootIndex(resolvedRoots, node, previewRoot);
            if (rootIndex < 0)
            {
                return;
            }

            BillboardRootDefinition definition = resolvedRoots[rootIndex].definition;
            string rootName = string.IsNullOrEmpty(definition.displayName)
                ? "(unnamed root)"
                : definition.displayName;

            if (resolvedRoots[rootIndex].node == node)
            {
                label.EnableInClassList(BillboardRootUssClassName, true);
                label.text = BillboardRootGlyph + label.text;
                label.tooltip = "Billboard root - " + ObjectNames.NicifyVariableName(
                    definition.mode.ToString());
                return;
            }

            label.EnableInClassList(BillboardInheritedUssClassName, true);
            label.text = BillboardInheritedGlyph + label.text;
            label.tooltip = "Billboards with «" + rootName + "»";
        }

        /// <summary>The preview transform a hierarchy row stands for, or null when it stands for none.</summary>
        internal Transform ResolveHierarchyTransform(HierarchyItem item)
        {
            if (item == null || previewController == null)
            {
                return null;
            }
            Transform root = previewController.HierarchyRoot;
            if (root == null)
            {
                return null;
            }

            switch (item.kind)
            {
                case HierarchyItemKind.RigTarget:
                    return PrefabAuthoringBridge.FindByName(root, item.displayName);
                default:
                    return previewController.GetTransformByIndex(item.previewIndex);
            }
        }

        // Sockets have no rows of their own; a socket whose source resolves to nothing has no row
        // to offer, which is what the clip inspector's socket list exists to catch.
        /// <summary>The hierarchy row a socket hangs off: the part or bone it follows.</summary>
        internal bool TryFindSocketSourceItemId(uint socketId, out int itemId)
        {
            itemId = NothingSelectedItemId;
            SocketDefinition socket = FindSocket(socketId);
            if (socket == null)
            {
                return false;
            }

            if (socket.mode == SocketAttachMode.RigTarget)
            {
                return TryFindRigTargetItemId(socket.targetId, out itemId);
            }

            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value.kind == HierarchyItemKind.RigTarget)
                {
                    continue;
                }
                if (string.Equals(
                        pair.Value.displayName, socket.boneName, System.StringComparison.Ordinal))
                {
                    itemId = pair.Key;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The row standing for a part, whichever pane it came from.</summary>
        internal bool TryFindRigTargetItemId(uint targetId, out int itemId)
        {
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (targetId != 0u && pair.Value.targetId == targetId)
                {
                    itemId = pair.Key;
                    return true;
                }
            }
            itemId = NothingSelectedItemId;
            return false;
        }

        /// <summary>How many transform and flipbook tracks the selected clip aims at a target, by tag when the track carries one.</summary>
        internal int CountTracksForTarget(uint targetId)
        {
            return AssetReferenceIndex.CountTracksBoundToTarget(session.SelectedClip, ActiveRig, targetId);
        }

        /// <summary>The single place a hierarchy selection takes effect, whichever surface caused it.</summary>
        private void OnHierarchySelectionChanged(IEnumerable<object> selection)
        {
            // Re-entry guard, and not an optional one. A selection change rebuilds the timeline,
            // which calls RefreshItems to redraw the tree's "animated" marks, and RefreshItems
            // re-resolves the tree's own selection — which can notify again. Without this the two
            // call each other until the stack runs out.
            if (isHandlingHierarchySelection)
            {
                return;
            }
            isHandlingHierarchySelection = true;
            try
            {
                ApplyHierarchySelectionChange();
            }
            finally
            {
                isHandlingHierarchySelection = false;
            }

        }

        // Active is the row just added, found by diffing against the previous selection rather than
        // taking the last of selectedIndices, which is ordered by row position, not click order.
        /// <summary>Adopts the tree's whole selection and works out which row of it is active.</summary>
        private void ApplyHierarchySelectionChange()
        {
            // An echo is not a click. RefreshItems re-resolves the tree's selection and notifies
            // with the set that is already applied; taking that for a user action cleared the key
            // selection a moment after the click that made it, so clicking a key showed the bone
            // panel instead of the key. Suppressing the notification at the RefreshItems call is
            // only half the fix — it cannot cover a notification the tree defers to a later frame.
            if (IsHierarchySelectionEcho())
            {
                return;
            }

            int previousActiveItemId = selectedHierarchyItemId;

            selectedHierarchyItems.Clear();
            int newlySelectedItemId = NothingSelectedItemId;
            bool previousActiveIsStillSelected = false;

            foreach (int selectedIndex in hierarchyTreeView.selectedIndices)
            {
                int itemId = hierarchyTreeView.GetIdForIndex(selectedIndex);
                HierarchyItem item;
                if (!hierarchyItemsById.TryGetValue(itemId, out item))
                {
                    continue;
                }
                selectedHierarchyItems.Add(item);

                if (itemId == previousActiveItemId)
                {
                    previousActiveIsStillSelected = true;
                }
                else if (!previouslySelectedItemIds.Contains(itemId))
                {
                    newlySelectedItemId = itemId;
                }
            }

            // A row was added: that is the one just clicked. Nothing was added (a row was removed
            // instead, or the whole range was replaced): keep the active row if it survived, else
            // fall back to the first of what is left.
            if (newlySelectedItemId != NothingSelectedItemId)
            {
                activeHierarchyItemId = newlySelectedItemId;
            }
            else if (!previousActiveIsStillSelected)
            {
                activeHierarchyItemId = selectedHierarchyItems.Count > 0
                    ? FindItemIdOf(selectedHierarchyItems[0])
                    : NothingSelectedItemId;
            }

            previouslySelectedItemIds.Clear();
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                previouslySelectedItemIds.Add(FindItemIdOf(selectedHierarchyItems[itemIndex]));
            }

            ApplyHierarchySelection();

            // Key selection and hierarchy selection are one selection with two sources: showing a
            // key's values under a heading naming a different object would be a lie about what the
            // fields edit. The window clears the keys and rebuilds the timeline and the inspector.
            TreeSelectionChanged?.Invoke();
        }

        // Compared by resolved item, not only by id: a rebuilt hierarchy can hand out the same ids
        // for freshly constructed items, and treating that as an echo would leave
        // selectedHierarchyItems holding detached objects the inspector would then edit.
        /// <summary>Whether the tree is reporting the selection that has already been applied.</summary>
        private bool IsHierarchySelectionEcho()
        {
            if (hierarchyTreeView == null)
            {
                return false;
            }

            int matchedCount = 0;
            foreach (int selectedIndex in hierarchyTreeView.selectedIndices)
            {
                int itemId = hierarchyTreeView.GetIdForIndex(selectedIndex);
                HierarchyItem item;
                if (!hierarchyItemsById.TryGetValue(itemId, out item))
                {
                    return false;
                }
                if (!previouslySelectedItemIds.Contains(itemId)
                    || !selectedHierarchyItems.Contains(item))
                {
                    return false;
                }
                matchedCount++;
            }

            // Counts as well as membership, so a selection that shrank is not mistaken for an echo
            // of the larger one it came from.
            return matchedCount == previouslySelectedItemIds.Count
                && matchedCount == selectedHierarchyItems.Count;
        }

        // RefreshItems re-resolves the tree's selection while redrawing it, which raises
        // selectionChanged — every call here is only a redraw and must not reach the selection handler.
        /// <summary>Repaints the tree's rows without letting the repaint pose as a selection change.</summary>
        internal void RefreshHierarchyRows()
        {
            if (hierarchyTreeView == null)
            {
                return;
            }
            bool wasHandlingSelection = isHandlingHierarchySelection;
            isHandlingHierarchySelection = true;
            try
            {
                hierarchyTreeView.RefreshItems();
            }
            finally
            {
                isHandlingHierarchySelection = wasHandlingSelection;
            }
        }

        /// <summary>
        /// Selects one row and nothing else — the viewport's click path and the timeline's.
        /// </summary>
        internal void SelectHierarchyItem(int itemId)
        {
            selectedHierarchyItems.Clear();
            previouslySelectedItemIds.Clear();
            activeHierarchyItemId = NothingSelectedItemId;

            HierarchyItem item;
            if (hierarchyItemsById.TryGetValue(itemId, out item))
            {
                selectedHierarchyItems.Add(item);
                previouslySelectedItemIds.Add(itemId);
                activeHierarchyItemId = itemId;
            }
            ApplyHierarchySelection();
        }

        /// <summary>Points the viewport outline and the gizmo at the active row, or at nothing.</summary>
        private void ApplyHierarchySelection()
        {
            // A held edit belongs to the part it was made on; changing the selection ends it: the
            // window answers this notification by discarding it.
            session.SetHierarchySelection(selectedHierarchyItems, ActiveHierarchyItem);

            HierarchyItem activeItem = ActiveHierarchyItem;
            if (activeItem == null)
            {
                selectedHierarchyItemId = NothingSelectedItemId;
                selectedBoneName = null;
                selectedTargetId = 0u;

                // A socket is reached through the object carrying it, so selecting nothing leaves
                // the gizmo nothing to be on.
                selectedSocketId = 0u;
                if (previewController != null)
                {
                    previewController.SetSelectedHierarchyIndex(-1);
                    previewController.SetSelectedSocketId(0u);
                }
                return;
            }

            selectedHierarchyItemId = FindItemIdOf(activeItem);
            // Selecting an object drops whichever socket the gizmo was on, unless that socket is
            // one of this object's own components — the gizmo has to be on something the selection
            // can still see.
            if (!SocketBelongsToItem(selectedSocketId, activeItem))
            {
                selectedSocketId = 0u;
            }
            if (previewController != null)
            {
                previewController.SetSelectedSocketId(selectedSocketId);
            }
            // targetId is set on a RigTarget row always, and on a PrefabTransform row whenever a
            // part claims that node — a claimed part is as much a clip-authoring target as a rig
            // target row is (see HierarchyItem.targetId), so the gizmo/drag key on it either way.
            selectedTargetId = activeItem.targetId;
            if (activeItem.kind == HierarchyItemKind.RigTarget)
            {
                selectedBoneName = null;
                if (previewController != null)
                {
                    previewController.SetSelectedTargetId(activeItem.targetId);
                }
                return;
            }

            selectedBoneName = activeItem.displayName;
            if (previewController != null)
            {
                previewController.SetSelectedHierarchyIndex(activeItem.previewIndex);
            }
        }

        /// <summary>The row the gizmo and the outline follow: the one most recently added.</summary>
        internal HierarchyItem ActiveHierarchyItem
        {
            get
            {
                HierarchyItem item;
                if (activeHierarchyItemId != NothingSelectedItemId
                    && hierarchyItemsById.TryGetValue(activeHierarchyItemId, out item)
                    && selectedHierarchyItems.Contains(item))
                {
                    return item;
                }
                return selectedHierarchyItems.Count > 0 ? selectedHierarchyItems[0] : null;
            }
        }

        private int FindItemIdOf(HierarchyItem item)
        {
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value == item)
                {
                    return pair.Key;
                }
            }
            return NothingSelectedItemId;
        }

        /// <summary>Whether a transform or flipbook track's target is in the current selection.</summary>
        internal bool IsTargetSelected(uint targetId)
        {
            if (targetId == 0u)
            {
                return false;
            }
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                if (selectedHierarchyItems[itemIndex].targetId == targetId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether a bone track's bone is in the current selection.</summary>
        internal bool IsBoneSelected(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                HierarchyItem item = selectedHierarchyItems[itemIndex];
                if (item.kind == HierarchyItemKind.PrefabTransform && item.displayName == boneName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Names what the timeline is focused on, for the status line.</summary>
        internal string DescribeSelection()
        {
            if (selectedHierarchyItems.Count == 0)
            {
                return "nothing";
            }
            if (selectedHierarchyItems.Count > 2)
            {
                return selectedHierarchyItems.Count.ToString() + " objects";
            }

            string described = string.Empty;
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                if (itemIndex > 0)
                {
                    described += " + ";
                }
                described += DescribeHierarchyItemName(selectedHierarchyItems[itemIndex]);
            }
            return described;
        }

        private string DescribeHierarchyItemName(HierarchyItem item)
        {
            return item.kind == HierarchyItemKind.RigTarget
                ? ResolveTargetDisplayName(item.targetId)
                : item.displayName;
        }

        /// <summary>Deselects everywhere at once — tree, viewport outline and inspector.</summary>
        internal void ClearHierarchySelection()
        {
            // Not "< 0": rig-target rows carry negative ids, so a less-than test would treat every
            // selected part as nothing selected and refuse to clear it.
            if (selectedHierarchyItemId == NothingSelectedItemId)
            {
                return;
            }

            // Without notifying, because the clearing this would trigger is exactly what the rest of
            // this method does — and re-entering it would clear a key selection that a viewport
            // click on empty space has no business touching.
            if (hierarchyTreeView != null)
            {
                hierarchyTreeView.SetSelectionWithoutNotify(new int[0]);
            }
            SelectHierarchyItem(NothingSelectedItemId);
            // The timeline is rebuilt too: with nothing selected the focus filter lifts, and the
            // rows it was hiding have to come back or clearing the selection would look like it
            // deleted them. Both rebuilds are the window's.
            SelectionCleared?.Invoke();
        }

        internal int FindBoneTrackIndex(string boneName)
        {
            if (session.SelectedClip == null || session.SelectedClip.boneTracks == null || string.IsNullOrEmpty(boneName))
            {
                return -1;
            }
            for (int trackIndex = 0; trackIndex < session.SelectedClip.boneTracks.Count; trackIndex++)
            {
                BoneTrack track = session.SelectedClip.boneTracks[trackIndex];
                if (track != null && track.boneName == boneName)
                {
                    return trackIndex;
                }
            }
            return -1;
        }

        // The hierarchy row owning a key's track, or null when the object has no row. A bone is
        // matched by name and a part by id, since a bone lives in an imported hierarchy this
        // package never assigned an id to.
        internal HierarchyItem FindHierarchyItemForKey(KeyAddress address)
        {
            if (session.SelectedClip == null)
            {
                return null;
            }

            if (address.trackKind == TimelineTrackKind.Bone)
            {
                if (session.SelectedClip.boneTracks == null
                    || address.trackIndex < 0
                    || address.trackIndex >= session.SelectedClip.boneTracks.Count)
                {
                    return null;
                }
                string boneName = session.SelectedClip.boneTracks[address.trackIndex].boneName;
                foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
                {
                    if (pair.Value.kind != HierarchyItemKind.RigTarget
                        && string.Equals(
                            pair.Value.displayName, boneName, System.StringComparison.Ordinal))
                    {
                        return pair.Value;
                    }
                }
                return null;
            }

            uint targetId = 0u;
            if (address.trackKind == TimelineTrackKind.Transform)
            {
                if (session.SelectedClip.transformTracks == null
                    || address.trackIndex < 0
                    || address.trackIndex >= session.SelectedClip.transformTracks.Count)
                {
                    return null;
                }
                targetId = session.SelectedClip.transformTracks[address.trackIndex].targetId;
            }
            else if (address.trackKind == TimelineTrackKind.Sprite)
            {
                if (session.SelectedClip.spriteTracks == null
                    || address.trackIndex < 0
                    || address.trackIndex >= session.SelectedClip.spriteTracks.Count)
                {
                    return null;
                }
                targetId = session.SelectedClip.spriteTracks[address.trackIndex].targetId;
            }

            int itemId;
            if (targetId == 0u || !TryFindRigTargetItemId(targetId, out itemId))
            {
                return null;
            }
            HierarchyItem item;
            return hierarchyItemsById.TryGetValue(itemId, out item) ? item : null;
        }

        /// <summary>Whether a socket is one of this row's own components.</summary>
        private bool SocketBelongsToItem(uint socketId, HierarchyItem item)
        {
            if (socketId == 0u || item == null)
            {
                return false;
            }
            SocketDefinition socket = FindSocket(socketId);
            if (socket == null)
            {
                return false;
            }
            if (socket.mode == SocketAttachMode.RigTarget)
            {
                // On the id, not the row's kind: a claimed node is the part its sockets follow.
                return item.targetId != 0u && socket.targetId == item.targetId;
            }
            return item.kind != HierarchyItemKind.RigTarget
                && string.Equals(socket.boneName, item.displayName, System.StringComparison.Ordinal);
        }
    }
}
