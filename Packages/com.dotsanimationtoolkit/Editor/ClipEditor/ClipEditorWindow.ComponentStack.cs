// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The inspector's component stack: what an object carries on this clip, added and removed the
    /// way components are added to and removed from a GameObject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The stack is a view of the asset, not a second copy of it.</strong> A component is
    /// present exactly when the track it stands for exists (<see cref="ClipComponentModel"/>), so
    /// there is no list of components to keep in step with the tracks and no migration for clips
    /// authored before this existed — an old clip opens showing precisely what it animates.
    /// </para>
    /// <para>
    /// <strong>Adding is a decision separate from keying.</strong> Add Transform and the object has
    /// an empty transform track: nothing plays yet, but the channel is declared, and the fields to
    /// key it are on screen. Before this, a track appeared the first time somebody happened to drag
    /// a field, which meant the answer to "does this clip animate this part" was a thing you found
    /// out by accident.
    /// </para>
    /// <para>
    /// <strong>Easing is not in the stack.</strong> It belongs to a key, not to an object — every
    /// key has one whether or not anyone chose it — so it is shown in the key block instead. The
    /// other four kinds are per-object channels, and Socket is per-object structure the rig owns,
    /// which is why it is badged: moving it while looking at one clip moves it in all of them.
    /// </para>
    /// </remarks>
    public sealed partial class ClipEditorWindow
    {
        private const string ComponentBlockUssClassName = "clip-editor__component";
        private const string ComponentHeaderUssClassName = "clip-editor__component-header";
        private const string ComponentTitleUssClassName = "clip-editor__component-title";
        private const string ComponentBadgeUssClassName = "clip-editor__component-badge";
        private const string ComponentBodyUssClassName = "clip-editor__component-body";
        private const string ComponentActiveUssClassName = "clip-editor__component--active";
        private const string ComponentRemoveUssClassName = "clip-editor__component-remove";
        private const string AddComponentUssClassName = "clip-editor__add-component";

        private const string ExpandedGlyph = "▾ ";
        private const string CollapsedGlyph = "▸ ";

        private readonly List<ClipComponentInstance> componentInstances =
            new List<ClipComponentInstance>();

        private readonly List<BillboardTrack> billboardTracks = new List<BillboardTrack>();
        private readonly List<int> billboardTrackIndices = new List<int>();

        /// <summary>
        /// Kinds the author has folded away, remembered across rebuilds.
        /// </summary>
        /// <remarks>
        /// By kind rather than by instance: the panel is rebuilt on every edit and every selection
        /// change, so per-instance state would be forgotten the moment a track index shifted, and
        /// "I do not want to look at flipbooks right now" is a statement about flipbooks rather than
        /// about one track.
        /// </remarks>
        private readonly HashSet<ClipComponentKind> collapsedComponentKinds =
            new HashSet<ClipComponentKind>();

        private RigAsset ActiveRig
        {
            get { return clipSet != null ? clipSet.rig : null; }
        }

        /// <summary>
        /// The component stack for one selected object, headed by its name.
        /// </summary>
        private void BuildComponentStack(HierarchyItem item, bool isActive)
        {
            ClipObjectRef objectRef = BuildObjectRef(item);

            inspectorPane.Add(MakeSelectionHeading(
                item.kind == HierarchyItemKind.RigTarget
                    ? ResolveTargetDisplayName(item.targetId)
                    : item.displayName,
                isActive));

            AddObjectKindHint(item);

            if (isActive && item.kind == HierarchyItemKind.RigTarget)
            {
                // Keyed off the window's active target rather than the heading's marker: the marker
                // is suppressed when there is only one block, but that block is still the one with a
                // gizmo.
                RefreshGizmo();
            }

            ClipComponentModel.CollectInstances(
                selectedClip, ActiveRig, objectRef, componentInstances);

            for (int instanceIndex = 0; instanceIndex < componentInstances.Count; instanceIndex++)
            {
                ClipComponentInstance instance = componentInstances[instanceIndex];
                inspectorPane.Add(BuildComponentBlock(objectRef, instance));
            }

            if (componentInstances.Count == 0)
            {
                inspectorPane.Add(MakeHint(selectedClip == null
                    ? "Select a clip, then add a component to animate this object."
                    : "Nothing on this object yet. Add a component to animate it — Transform for a "
                        + "part, Bone Transform for a bone."));
            }

            inspectorPane.Add(BuildAddComponentButton(objectRef));
        }

        /// <summary>Says what kind of thing the selected row is, which decides what it can carry.</summary>
        private void AddObjectKindHint(HierarchyItem item)
        {
            if (item.kind == HierarchyItemKind.RigTarget)
            {
                inspectorPane.Add(MakeHint("Rig target — a cutout part this rig declares."));
                return;
            }

            // The hierarchy lists every transform, not only bones, so the inspector says which kind
            // this one is. What you can usefully do with it depends on the answer: only a skinned
            // bone moves the mesh when a bone track drives it.
            if (previewController == null)
            {
                return;
            }
            string description = previewController.DescribeHierarchyItem(item.previewIndex);
            if (!string.IsNullOrEmpty(description))
            {
                inspectorPane.Add(MakeHint(description));
            }
        }

        /// <summary>
        /// The object a stack belongs to, with its billboard root resolved.
        /// </summary>
        /// <remarks>
        /// The root id is resolved here rather than inside the model because a bone's root is
        /// addressed by hierarchy path, and only the window knows the previewed hierarchy that path
        /// is read against.
        /// </remarks>
        private ClipObjectRef BuildObjectRef(HierarchyItem item)
        {
            uint billboardRootId = 0u;
            RigAsset rig = ActiveRig;
            if (rig != null)
            {
                int rootIndex = FindBillboardRootIndexFor(rig, item);
                if (rootIndex >= 0 && rig.billboardRoots[rootIndex] != null)
                {
                    billboardRootId = rig.billboardRoots[rootIndex].Id.Value;
                }
            }

            if (item.kind == HierarchyItemKind.RigTarget)
            {
                return ClipObjectRef.RigTarget(item.targetId, billboardRootId);
            }

            // A bone is addressed by path, and an empty path means the prefab root — a real answer,
            // not a missing one. So the check is whether there is a hierarchy to read a path
            // against at all, rather than whether the path came back empty.
            bool billboardAddressable =
                previewController != null && previewController.HierarchyRoot != null;
            return ClipObjectRef.Bone(
                item.displayName, billboardRootId, ResolveHierarchyPath(item), billboardAddressable);
        }

        /// <summary>One component: a header that folds it away, and the fields it owns.</summary>
        private VisualElement BuildComponentBlock(
            ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            VisualElement block = new VisualElement();
            block.AddToClassList(ComponentBlockUssClassName);

            bool isExpanded = !collapsedComponentKinds.Contains(instance.kind);

            VisualElement body = new VisualElement();
            body.AddToClassList(ComponentBodyUssClassName);
            body.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            VisualElement header = new VisualElement();
            header.AddToClassList(ComponentHeaderUssClassName);

            Label title = new Label(
                (isExpanded ? ExpandedGlyph : CollapsedGlyph)
                + DescribeComponent(objectRef, instance));
            title.AddToClassList(ComponentTitleUssClassName);
            title.RegisterCallback<PointerDownEvent>(pointerEvent =>
            {
                ToggleComponentKind(instance.kind);
                pointerEvent.StopPropagation();
            });
            header.Add(title);

            if (ClipComponentModel.Scope(instance.kind) == ClipComponentScope.Rig)
            {
                Label badge = new Label("rig-wide");
                badge.AddToClassList(ComponentBadgeUssClassName);
                badge.tooltip =
                    "Stored on the rig asset, so every clip in the set sees this one. Moving it "
                    + "here moves it everywhere.";
                header.Add(badge);
            }

            Button removeButton = new Button(() => ConfirmRemoveComponent(objectRef, instance));
            removeButton.text = "✕";
            removeButton.tooltip = "Remove " + ClipComponentModel.DisplayName(instance.kind) + ".";
            removeButton.AddToClassList(ComponentRemoveUssClassName);
            header.Add(removeButton);

            block.Add(header);

            if (isExpanded)
            {
                BuildComponentBody(body, objectRef, instance);
            }
            block.Add(body);

            // A socket block marks itself when it is the one the viewport gizmo is on, which is the
            // only way to tell which of several sockets a drag would move.
            if (instance.kind == ClipComponentKind.Socket)
            {
                SocketDefinition socket = ResolveSocket(instance);
                bool isGizmoTarget = socket != null && socket.Id.Value == selectedSocketId;
                block.EnableInClassList(ComponentActiveUssClassName, isGizmoTarget);
            }

            return block;
        }

        /// <summary>The component's name, plus what it holds when that is worth knowing up front.</summary>
        private string DescribeComponent(ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            string name = ClipComponentModel.DisplayName(instance.kind);
            if (instance.kind == ClipComponentKind.Socket)
            {
                SocketDefinition socket = ResolveSocket(instance);
                if (socket != null && !string.IsNullOrEmpty(socket.displayName))
                {
                    return name + "  ·  " + socket.displayName;
                }
                return name;
            }

            int keyCount = ClipComponentModel.KeyCount(selectedClip, objectRef, instance);
            return name + "  ·  " + keyCount + " key(s)";
        }

        private void ToggleComponentKind(ClipComponentKind kind)
        {
            if (!collapsedComponentKinds.Remove(kind))
            {
                collapsedComponentKinds.Add(kind);
            }
            RebuildInspector();
        }

        /// <summary>Fills a component's body with the fields that kind owns.</summary>
        private void BuildComponentBody(
            VisualElement body, ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            switch (instance.kind)
            {
                case ClipComponentKind.Transform:
                    AddTransformFields(body, objectRef.targetId);
                    return;

                case ClipComponentKind.BoneTransform:
                {
                    BoneTrack track = ResolveBoneTrack(instance);
                    if (track != null)
                    {
                        AddBoneTransformFields(body, track);
                    }
                    return;
                }

                case ClipComponentKind.Flipbook:
                {
                    SpriteTrack track = ResolveSpriteTrack(instance);
                    if (track != null)
                    {
                        body.Add(BuildFlipbookTrackBlock(track, instance.index));
                    }
                    return;
                }

                case ClipComponentKind.Billboard:
                    AddBillboardFields(body, objectRef);
                    return;

                default:
                {
                    SocketDefinition socket = ResolveSocket(instance);
                    if (socket != null)
                    {
                        AddSocketFields(body, socket);
                    }
                    return;
                }
            }
        }

        private BoneTrack ResolveBoneTrack(ClipComponentInstance instance)
        {
            if (selectedClip == null || selectedClip.boneTracks == null
                || instance.index < 0 || instance.index >= selectedClip.boneTracks.Count)
            {
                return null;
            }
            return selectedClip.boneTracks[instance.index];
        }

        private SpriteTrack ResolveSpriteTrack(ClipComponentInstance instance)
        {
            if (selectedClip == null || selectedClip.spriteTracks == null
                || instance.index < 0 || instance.index >= selectedClip.spriteTracks.Count)
            {
                return null;
            }
            return selectedClip.spriteTracks[instance.index];
        }

        private BillboardTrack ResolveBillboardTrack(int trackIndex)
        {
            if (selectedClip == null || selectedClip.billboardTracks == null
                || trackIndex < 0 || trackIndex >= selectedClip.billboardTracks.Count)
            {
                return null;
            }
            return selectedClip.billboardTracks[trackIndex];
        }

        private SocketDefinition ResolveSocket(ClipComponentInstance instance)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.sockets == null
                || instance.index < 0 || instance.index >= rig.sockets.Count)
            {
                return null;
            }
            return rig.sockets[instance.index];
        }

        // -------------------------------------------------------------------------------------
        // Adding and removing.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The Add Component button, and the menu of what this object could carry.
        /// </summary>
        /// <remarks>
        /// Kinds that do not fit are listed disabled with the reason attached rather than omitted: a
        /// menu that silently leaves out the thing you came looking for reads as a bug, and the
        /// reason is usually actionable — "make this node a billboard root first".
        /// </remarks>
        private VisualElement BuildAddComponentButton(ClipObjectRef objectRef)
        {
            Button addButton = new Button();
            addButton.text = "Add Component";
            addButton.AddToClassList(AddComponentUssClassName);
            addButton.clicked += () =>
            {
                GenericDropdownMenu menu = new GenericDropdownMenu();
                IReadOnlyList<ClipComponentKind> kinds = ClipComponentModel.AllKinds;
                for (int kindIndex = 0; kindIndex < kinds.Count; kindIndex++)
                {
                    ClipComponentKind kind = kinds[kindIndex];
                    string unavailableReason;
                    string label = ClipComponentModel.DisplayName(kind);
                    if (ClipComponentModel.CanAdd(
                            selectedClip, ActiveRig, objectRef, kind, out unavailableReason))
                    {
                        menu.AddItem(label, false, () => AddComponent(objectRef, kind));
                        continue;
                    }
                    menu.AddDisabledItem(label + "  —  " + unavailableReason, false);
                }
                menu.DropDown(addButton.worldBound, addButton, true);
            };
            return addButton;
        }

        /// <summary>
        /// Creates the track or socket a component stands for, on the right undo stack.
        /// </summary>
        /// <remarks>
        /// A clip-scoped component records undo on the clip and a rig-scoped one on the rig, which
        /// is not a formality: putting a socket on the clip's undo stack would make an undo in one
        /// clip silently move an attachment in every other clip of the set.
        /// </remarks>
        private void AddComponent(ClipObjectRef objectRef, ClipComponentKind kind)
        {
            RigAsset rig = ActiveRig;
            if (ClipComponentModel.Scope(kind) == ClipComponentScope.Rig)
            {
                if (rig == null)
                {
                    return;
                }
                RecordSocketEdit(rig, "Add " + ClipComponentModel.DisplayName(kind));
                ClipComponentModel.Add(
                    selectedClip, rig, objectRef, kind, DescribeNewComponentName(objectRef, kind));

                // Minted here rather than left to OnValidate, which does not run in time. An id
                // still 0 is not merely unidentified: for a socket 0 is the sentinel for "none
                // selected", and a billboard root saved with it is one no track could address.
                rig.EnsureStableIds();
                AssetDatabase.SaveAssetIfDirty(rig);
                CommitSocketEdit(true);

                // A billboard root changes how the node renders in the preview, and marks its row.
                RefreshHierarchyRows();
                RebuildInspector();
                return;
            }

            if (selectedClip == null)
            {
                return;
            }
            RecordClipEdit("Add " + ClipComponentModel.DisplayName(kind));
            ClipComponentModel.Add(selectedClip, rig, objectRef, kind, string.Empty);
            CommitClipEdit();

            // A new track is a new lane, and the timeline is where its keys will be made.
            RebuildTimeline();
            RebuildHierarchy();
            RebuildInspector();
        }

        /// <summary>
        /// The label a newly added rig-scoped component carries, built from its object's name.
        /// </summary>
        /// <remarks>
        /// A billboard root is named after the node outright, because it <em>is</em> that node
        /// facing the viewer. A socket takes the node's name plus "Socket", because a node can hang
        /// several off itself and they have to be told apart.
        /// </remarks>
        private string DescribeNewComponentName(ClipObjectRef objectRef, ClipComponentKind kind)
        {
            string sourceName = objectRef.kind == ClipObjectKind.RigTarget
                ? ResolveTargetDisplayName(objectRef.targetId)
                : objectRef.boneName;
            if (kind == ClipComponentKind.Billboard)
            {
                return sourceName;
            }
            return string.IsNullOrEmpty(sourceName) ? "New Socket" : sourceName + " Socket";
        }

        /// <summary>
        /// Removes a component, asking first when that would throw away authored work.
        /// </summary>
        /// <remarks>
        /// A track with no keys goes without a prompt — there is nothing in it to lose, and being
        /// asked about it would train the author to dismiss the dialog that matters. A track with
        /// keys, and every socket, asks: the keys are work, and a socket is rig structure something
        /// in a scene may already be attached to.
        /// </remarks>
        private void ConfirmRemoveComponent(ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            string kindName = ClipComponentModel.DisplayName(instance.kind);
            RigAsset rig = ActiveRig;

            if (instance.kind == ClipComponentKind.Socket)
            {
                SocketDefinition socket = ResolveSocket(instance);
                if (socket == null || rig == null)
                {
                    return;
                }
                if (!EditorUtility.DisplayDialog(
                        "Remove Socket",
                        "Remove \"" + DescribeSocketForPrompt(socket) + "\" from the rig?\n\n"
                            + "Every clip in this set loses it, and anything attached to it at run "
                            + "time will have nothing to follow.",
                        "Remove",
                        "Cancel"))
                {
                    return;
                }

                bool wasGizmoTarget = socket.Id.Value == selectedSocketId;
                RecordSocketEdit(rig, "Remove Socket");
                ClipComponentModel.Remove(selectedClip, rig, instance);
                AssetDatabase.SaveAssetIfDirty(rig);
                if (wasGizmoTarget)
                {
                    FocusSocket(0u);
                }
                CommitSocketEdit(true);
                RebuildInspector();
                return;
            }

            if (instance.kind == ClipComponentKind.Billboard)
            {
                ConfirmRemoveBillboard(objectRef, instance);
                return;
            }

            int keyCount = ClipComponentModel.KeyCount(selectedClip, objectRef, instance);
            if (keyCount > 0 && !EditorUtility.DisplayDialog(
                    "Remove " + kindName,
                    "Remove " + kindName + " from this object?\n\n"
                        + keyCount + " key(s) on this clip go with it.",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            RecordClipEdit("Remove " + kindName);
            ClipComponentModel.Remove(selectedClip, rig, instance);
            CommitClipEdit();

            // The removed track's keys may well be in the selection, and an address into a track
            // that no longer exists is a selection nothing can draw.
            selectedKeys.Clear();
            hasActiveKey = false;

            RebuildTimeline();
            RebuildHierarchy();
            RebuildInspector();
        }

        /// <summary>
        /// Removes a billboard root, and the keys of every clip track that addressed it.
        /// </summary>
        /// <remarks>
        /// The one component whose removal writes both assets, so both are recorded: the root is rig
        /// structure and the keys are this clip's. A track left bound to a root the rig no longer
        /// declares fails validation rule V24 and animates nothing, so it goes with it — which the
        /// prompt says out loud, because the keys are only visible in this component.
        /// </remarks>
        private void ConfirmRemoveBillboard(
            ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            RigAsset rig = ActiveRig;
            if (rig == null)
            {
                return;
            }

            int keyCount = ClipComponentModel.KeyCount(selectedClip, objectRef, instance);
            string keyWarning = keyCount > 0
                ? "\n\n" + keyCount + " key(s) on this clip go with it."
                : string.Empty;
            if (!EditorUtility.DisplayDialog(
                    "Remove Billboard",
                    "Stop this node facing the viewer?\n\n"
                        + "It is rig structure, so every clip in the set loses it." + keyWarning,
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            RecordSocketEdit(rig, "Remove Billboard");
            if (selectedClip != null)
            {
                RecordClipEdit("Remove Billboard");
            }
            ClipComponentModel.Remove(selectedClip, rig, instance);
            if (selectedClip != null)
            {
                CommitClipEdit();
            }
            AssetDatabase.SaveAssetIfDirty(rig);
            CommitSocketEdit(false);

            selectedKeys.Clear();
            hasActiveKey = false;
            RefreshHierarchyRows();
            RebuildTimeline();
            RebuildInspector();
        }

        private static string DescribeSocketForPrompt(SocketDefinition socket)
        {
            return string.IsNullOrEmpty(socket.displayName)
                ? "Socket " + socket.Id.Value.ToString()
                : socket.displayName;
        }

        /// <summary>
        /// Points the viewport gizmo at one socket, or at none.
        /// </summary>
        /// <remarks>
        /// Sockets have no row of their own in the hierarchy any more — they are components of the
        /// bone or part they follow — so this is what "which socket am I moving" now means. The
        /// marker and the gizmo both read it.
        /// </remarks>
        private void FocusSocket(uint socketId)
        {
            selectedSocketId = socketId;
            if (previewController != null)
            {
                previewController.SetSelectedSocketId(socketId);
            }
            RefreshGizmo();
        }

        /// <summary>
        /// The hierarchy row owning a key's track, or null when the object has no row.
        /// </summary>
        /// <remarks>
        /// A bone is matched by name and a part by id, which is the same asymmetry every other
        /// binding in the package carries: a bone lives in an imported hierarchy this package did
        /// not assign an id to.
        /// </remarks>
        private HierarchyItem FindHierarchyItemForKey(KeyAddress address)
        {
            if (selectedClip == null)
            {
                return null;
            }

            if (address.trackKind == TimelineTrackKind.Bone)
            {
                if (selectedClip.boneTracks == null
                    || address.trackIndex < 0
                    || address.trackIndex >= selectedClip.boneTracks.Count)
                {
                    return null;
                }
                string boneName = selectedClip.boneTracks[address.trackIndex].boneName;
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
                if (selectedClip.transformTracks == null
                    || address.trackIndex < 0
                    || address.trackIndex >= selectedClip.transformTracks.Count)
                {
                    return null;
                }
                targetId = selectedClip.transformTracks[address.trackIndex].targetId;
            }
            else if (address.trackKind == TimelineTrackKind.Sprite)
            {
                if (selectedClip.spriteTracks == null
                    || address.trackIndex < 0
                    || address.trackIndex >= selectedClip.spriteTracks.Count)
                {
                    return null;
                }
                targetId = selectedClip.spriteTracks[address.trackIndex].targetId;
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
            if (item.kind == HierarchyItemKind.RigTarget)
            {
                return socket.mode == SocketAttachMode.RigTarget
                    && socket.targetId == item.targetId;
            }
            return socket.mode == SocketAttachMode.Bone
                && string.Equals(socket.boneName, item.displayName, System.StringComparison.Ordinal);
        }

        // -------------------------------------------------------------------------------------
        // The rig's sockets, listed where nothing is selected.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Every socket on the rig, as a directory into the objects that carry them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Sockets are edited on their source, which means a socket whose source resolves to nothing
        /// has no stack to appear in — and an unresolved socket is exactly the one somebody needs to
        /// find, because at run time it pins its attachment to the actor's origin. This list is
        /// where it stays reachable: resolvable ones offer a jump to their source, and broken ones
        /// carry the binding fields and a delete, right here.
        /// </para>
        /// <para>
        /// Shown with nothing selected rather than beside a selection, because it is about the rig
        /// as a whole. It is the answer to "what attaches to this character", which is a question
        /// about the character, not about the part in front of you.
        /// </para>
        /// </remarks>
        private void AddSocketDirectory()
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.sockets == null || rig.sockets.Count == 0)
            {
                return;
            }

            inspectorPane.Add(MakeHeading("Attachment Points"));
            inspectorPane.Add(MakeHint(
                "Sockets live on the rig, and are edited on the part or bone they follow. "
                + rig.sockets.Count + " on this rig."));

            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket == null)
                {
                    continue;
                }
                inspectorPane.Add(BuildSocketDirectoryRow(rig, socket));
            }
        }

        private VisualElement BuildSocketDirectoryRow(RigAsset rig, SocketDefinition socket)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(ComponentBlockUssClassName);
            row.Add(MakeHeading(DescribeSocketLabel(socket)));

            int sourceItemId;
            if (TryFindSocketSourceItemId(socket.Id.Value, out sourceItemId))
            {
                row.Add(new Button(() => SelectSocketSource(socket))
                {
                    text = "Select Source",
                    tooltip = "Selects the object this socket follows and opens it here."
                });
                return row;
            }

            row.Add(MakeHint(
                "Follows nothing this rig has, so it has no object to be edited on. Rebind it "
                + "below, or remove it."));

            EnumField modeField = new EnumField("Follows", socket.mode);
            modeField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Change Socket Mode");
                socket.mode = (SocketAttachMode)changeEvent.newValue;
                CommitSocketEdit(true);
                RebuildInspector();
            });
            row.Add(modeField);

            if (socket.mode == SocketAttachMode.RigTarget)
            {
                row.Add(BuildSocketTargetField(rig, socket));
            }
            else if (previewController != null)
            {
                row.Add(BuildSocketBoneField(rig, socket));
            }
            else
            {
                row.Add(MakeHint(
                    "Assign a rigged prefab in the toolbar to pick the bone this should follow."));
            }

            row.Add(new Button(() => ConfirmDeleteSocket(socket))
            {
                text = "Delete Socket"
            });
            return row;
        }

        private void SelectSocketSource(SocketDefinition socket)
        {
            int sourceItemId;
            if (!TryFindSocketSourceItemId(socket.Id.Value, out sourceItemId)
                || hierarchyTreeView == null)
            {
                return;
            }
            hierarchyTreeView.SetSelectionById(sourceItemId);
            hierarchyTreeView.ScrollToItemById(sourceItemId);
            FocusSocket(socket.Id.Value);
            RebuildInspector();
        }

        // -------------------------------------------------------------------------------------
        // Billboard component body.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The billboard channels at the playhead, editable in place.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The component is the root; the track is optional.</strong> A node with this
        /// component faces the viewer in every clip whether or not anything animates it, so the
        /// fields open at the resting values and the first edit creates the track — the same
        /// bargain the flipbook body strikes with its first key.
        /// </para>
        /// <para>
        /// Editing keys at the playhead rather than holding the value pending, because there is no
        /// gizmo for a billboard angle and so nothing to show an unkeyed edit against.
        /// </para>
        /// <para>
        /// <strong>Billboard tracks have no timeline lane yet.</strong> The keys are real and the
        /// bake reads them; what is missing is a row to see and drag them on, so this block says how
        /// many there are rather than pretending the dopesheet shows them.
        /// </para>
        /// </remarks>
        private void AddBillboardFields(VisualElement parent, ClipObjectRef objectRef)
        {
            if (selectedClip == null)
            {
                parent.Add(MakeHint(
                    "This node faces the viewer. Select a clip to animate how much."));
                return;
            }

            ClipBillboardEditing.CollectTracksForRoot(
                selectedClip, objectRef.billboardRootId, billboardTracks, billboardTrackIndices);
            BillboardTrack track = billboardTracks.Count > 0 ? billboardTracks[0] : null;

            int keyCount = track != null && track.keys != null ? track.keys.Count : 0;
            bool isOnKey = track != null
                && ClipBillboardEditing.FindKeyIndexAt(track, playheadTime) >= 0;

            parent.Add(MakeHint(keyCount == 0
                ? "Facing the viewer, unanimated — editing a value below makes the first key."
                : (isOnKey
                    ? "On a key — editing changes this key."
                    : "Between keys — editing keys the value at the playhead.")));

            float angleOffsetDegrees;
            float blendWeight;
            bool enabled;
            ClipBillboardEditing.TryEvaluate(
                track, playheadTime, out angleOffsetDegrees, out blendWeight, out enabled);

            FloatField angleField = new FloatField("Angle Offset");
            angleField.tooltip =
                "Degrees off the resolved facing, about the billboard frame's own up axis. Added to "
                + "the root's rest offset rather than replacing it.";
            angleField.SetValueWithoutNotify(angleOffsetDegrees);
            angleField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBillboardEdit(objectRef, changeEvent.newValue, blendWeight, enabled);
            });
            parent.Add(angleField);

            Slider blendField = new Slider("Blend Weight", 0f, 1f);
            blendField.tooltip =
                "How much of the billboard orientation applies against the node's animated pose. "
                + "1 is fully billboarded; 0 hands the node back to its animation.";
            blendField.showInputField = true;
            blendField.SetValueWithoutNotify(blendWeight);
            blendField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBillboardEdit(objectRef, angleOffsetDegrees, changeEvent.newValue, enabled);
            });
            parent.Add(blendField);

            Toggle enabledField = new Toggle("Billboarding");
            enabledField.tooltip =
                "Held from its key, never eased — an enable flag is an instruction that fires at a "
                + "moment, not an approximation of anything between two moments.";
            enabledField.SetValueWithoutNotify(enabled);
            enabledField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBillboardEdit(objectRef, angleOffsetDegrees, blendWeight, changeEvent.newValue);
            });
            parent.Add(enabledField);

            parent.Add(MakeHint(
                keyCount + " key(s). Billboard keys have no timeline row yet — they are edited "
                + "here, at the playhead."));
        }

        /// <summary>
        /// Keys the billboard channels at the playhead, creating the track on the first edit.
        /// </summary>
        private void ApplyBillboardEdit(
            ClipObjectRef objectRef, float angleOffsetDegrees, float blendWeight, bool enabled)
        {
            if (selectedClip == null || objectRef.billboardRootId == 0u)
            {
                return;
            }

            RecordClipEdit("Edit Billboard");

            ClipBillboardEditing.CollectTracksForRoot(
                selectedClip, objectRef.billboardRootId, billboardTracks, billboardTrackIndices);
            BillboardTrack track = billboardTracks.Count > 0 ? billboardTracks[0] : null;
            if (track == null)
            {
                if (selectedClip.billboardTracks == null)
                {
                    selectedClip.billboardTracks = new List<BillboardTrack>();
                }
                track = new BillboardTrack();
                track.rootStableId = objectRef.billboardRootId;
                selectedClip.billboardTracks.Add(track);
            }

            ClipBillboardEditing.SetKeyValues(
                track, playheadTime, angleOffsetDegrees, blendWeight, enabled);
            CommitClipEdit();
            RebuildInspector();
        }
    }
}
