// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
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
    /// <strong>Adding is a decision separate from keying.</strong> Add a Flipbook and the object has
    /// an empty sprite track: nothing plays yet, but the channel is declared, and the fields to key
    /// it are on screen. Before this, a track appeared the first time somebody happened to drag a
    /// field, which meant the answer to "does this clip animate this part" was a thing you found out
    /// by accident.
    /// </para>
    /// <para>
    /// <strong>Transform is not one of the add-ons.</strong> Everything in the animator is
    /// somewhere, so every object's stack opens with its transform whether or not it has been keyed
    /// — a part's on a transform track, anything else's on a bone track — and it carries no remove
    /// button, because there is no state in which an object has no transform. The Add Component
    /// menu therefore offers Flipbook, Billboard and Socket, and those are the only things a person
    /// decides to put on an object.
    /// </para>
    /// <para>
    /// <strong>Easing is not in the stack.</strong> It belongs to a key, not to an object — every
    /// key has one whether or not anyone chose it — so it is shown in the key block instead. Socket
    /// and Billboard are per-object structure the rig owns, which is why they are badged: moving one
    /// while looking at one clip moves it in all of them.
    /// </para>
    /// </remarks>
    public sealed partial class ClipEditorWindow
    {
        private const string ComponentBlockUssClassName = "clip-editor__component";

        /// <summary>
        /// The object's own Transform or BoneTransform, styled apart from the add-on components
        /// below it. <see cref="ClipComponentKind"/>'s own doc comment already says why: it is
        /// intrinsic, never in the Add Component menu, never removable -- this is that same fact
        /// applied to the block's presentation, not a second decision.
        /// </summary>
        private const string ComponentIntrinsicUssClassName = "clip-editor__component--intrinsic";

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

        /// <summary>Rebuilt each time the picker opens, which is once per click and not per frame.</summary>
        private readonly List<ClipComponentPickerEntry> componentPickerEntries =
            new List<ClipComponentPickerEntry>();

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

        /// <summary>
        /// The rig this window is playing the open set against — window state, picked in the toolbar
        /// and stored on no asset. Independent of <see cref="clipSet"/> in both directions.
        /// </summary>
        private RigAsset ActiveRig
        {
            get { return activeRig; }
        }

        /// <summary>
        /// The component stack for one selected object, headed by its name.
        /// </summary>
        private void BuildComponentStack(HierarchyItem item, bool isActive)
        {
            ClipObjectRef objectRef = BuildObjectRef(item);

            SelectionHeadingElement heading = MakeSelectionHeading(
                item.kind == HierarchyItemKind.RigTarget
                    ? ResolveTargetDisplayName(item.targetId)
                    : item.displayName,
                isActive);
            DescribeSelectedObject(heading.label, item, objectRef);
            BindPartTagButton(heading, item);
            inspectorPane.Add(heading);

            // Keyed off ActiveHierarchyItem directly rather than the isActive parameter: that flag
            // is nulled out by the caller for a single selection to suppress the "(active)" marker
            // in the heading (there is nothing to disambiguate with one block on screen), but a
            // single selection is still exactly the block a gizmo belongs on. Reusing isActive here
            // meant a lone selection never refreshed the gizmo at all -- a stale or hidden gizmo is
            // indistinguishable from a broken one, which is the "no gizmos appeared" report this
            // fixes at its root, not only for Rig Edit.
            //
            // Rig Edit always qualifies, whatever the node's kind, per GizmoDragRouting's rule: it
            // writes the prefab's base pose for any selected node, target or not. Outside Rig Edit
            // the RigTarget-kind gate is unchanged -- clip authoring's gizmo still needs a declared
            // target and a selected clip, checked inside RefreshGizmo itself.
            if (item == ActiveHierarchyItem
                && (item.kind == HierarchyItemKind.RigTarget || IsRigEditMode))
            {
                RefreshGizmo();
            }

            ClipComponentModel.CollectInstances(
                selectedClip, ActiveRig, objectRef, componentInstances);

            for (int instanceIndex = 0; instanceIndex < componentInstances.Count; instanceIndex++)
            {
                ClipComponentInstance instance = componentInstances[instanceIndex];
                inspectorPane.Add(BuildComponentBlock(objectRef, instance));
            }

            // Said on the absence of a clip rather than on an empty stack, which no longer happens:
            // every object has a transform, so the stack is never empty and "nothing here" stopped
            // being able to mean "nothing to read it from".
            if (selectedClip == null)
            {
                inspectorPane.Add(MakeHint(
                    "No clip selected, so nothing here is keyed. Pick one to animate this object."));
            }

            inspectorPane.Add(BuildAddComponentButton(objectRef));
        }

        /// <summary>
        /// Says what kind of thing the selected row is, on the heading's hover rather than under it.
        /// </summary>
        /// <remarks>
        /// It answers a question that is asked once — "what am I looking at" — and then sits there
        /// being re-read for the rest of the session, pushing the components it describes further
        /// down a pane that is already short. Hovering asks for it; the stack keeps the room.
        /// </remarks>
        private void DescribeSelectedObject(Label heading, HierarchyItem item, ClipObjectRef objectRef)
        {
            if (heading == null)
            {
                return;
            }

            if (item.kind == HierarchyItemKind.RigTarget)
            {
                heading.tooltip = "Rig target — a cutout part this rig declares.";
                return;
            }

            // The hierarchy lists every transform, not only bones, so this says which kind this one
            // is. What you can usefully do with it depends on the answer: only a skinned bone moves
            // the mesh when a bone track drives it.
            string description = previewController != null
                ? previewController.DescribeHierarchyItem(item.previewIndex)
                : string.Empty;

            // A claimed node is a part as well as a node, and that is the more consequential half:
            // it decides which track its poses land on and whether a flipbook can bind to it.
            if (objectRef.HasRigTarget)
            {
                string partLine = "Declared a rig part by this rig, so it is posed on a transform "
                    + "track and can carry a flipbook.";
                heading.tooltip = string.IsNullOrEmpty(description)
                    ? partLine
                    : description + "\n\n" + partLine;
                return;
            }
            heading.tooltip = description;
        }

        // -----------------------------------------------------------------------------------
        // Part tag (Phase E target-tags spec §4.2): the selection heading's tag button, writing
        // RigTargetDefinition.tagId — "what is this part for", shared across every clip set that
        // uses this rig. Not to be confused with BuildTagBindButton below, which switches one
        // track's own binding between target id and tag and writes the clip instead of the rig.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Shows the part-tag button on a claimed rig target's heading, hidden otherwise — an
        /// unclaimed prefab node has no <see cref="RigTargetDefinition"/> to carry a tag.
        /// </summary>
        private void BindPartTagButton(SelectionHeadingElement heading, HierarchyItem item)
        {
            RigTargetDefinition target =
                item.targetId != 0u ? FindRigTargetById(item.targetId) : null;
            if (target == null)
            {
                heading.tagButton.style.display = DisplayStyle.None;
                return;
            }
            heading.tagButton.style.display = DisplayStyle.Flex;
            heading.tagButton.text = DescribePartTagButtonText(target.tagId);
            heading.tagButton.clicked += () => OpenPartTagPicker(target, heading.tagButton);
        }

        private string DescribePartTagButtonText(uint tagId)
        {
            if (tagId == 0u)
            {
                return "Tag: (none)";
            }
            TargetTagRegistry tagRegistry = ResolveTargetTagRegistry();
            string tagName = tagRegistry != null ? tagRegistry.FindName(tagId) : null;
            return tagName != null
                ? "Tag: " + tagName
                : "Tag: (unresolved 0x" + tagId.ToString("X8") + ")";
        }

        /// <summary>
        /// Opens the searchable tag picker anchored to a part's tag button in the selection
        /// heading — one popup style shared with <see cref="RigAssetEditor"/>'s Target Tags
        /// section and <see cref="BuildTagBindButton"/> below (spec §4.2.1).
        /// </summary>
        private void OpenPartTagPicker(RigTargetDefinition target, Button anchor)
        {
            RigAsset activeRig = ActiveRig;
            if (target == null || activeRig == null)
            {
                return;
            }

            TargetTagRegistry tagRegistry = ResolveTargetTagRegistry();
            VocabularyPicker.Open(
                rootVisualElement,
                anchor,
                tagRegistry,
                tagRegistry,
                VocabularyPickerConfig.ForTargetTags(tagRegistry),
                // Not straight to the field: assigning here used to leave rule T1 unenforced (two
                // parts wearing one tag), leave the timeline showing the binding it had before the
                // pick, and leave the part's keys behind on the tag it no longer wears.
                chosenTagId => RetagRigPart(target, chosenTagId),
                () =>
                {
                    // The registry changed underneath every open heading (a tag renamed or newly
                    // created via "Edit..." / "Create tag..."), so the whole inspector's labels
                    // are re-derived rather than just this one's.
                    RebuildInspector();
                });
        }

        /// <summary>
        /// The object a stack belongs to, with its billboard root and its rig part resolved.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both are resolved here rather than inside the model because both are addressed by
        /// hierarchy path, and only the window knows the previewed hierarchy that path is read
        /// against.
        /// </para>
        /// <para>
        /// <strong>A previewed node carries a target id whenever some part claims its path.</strong>
        /// That is what makes a plane in the prefab hierarchy something a flipbook can bind to: the
        /// id is the binding, and until a part records which node it stands for, a node has none.
        /// </para>
        /// </remarks>
        private ClipObjectRef BuildObjectRef(HierarchyItem item)
        {
            uint billboardRootId = 0u;
            uint ragdollBodyId = 0u;
            RigAsset rig = ActiveRig;
            if (rig != null)
            {
                int rootIndex = FindBillboardRootIndexFor(rig, item);
                if (rootIndex >= 0 && rig.billboardRoots[rootIndex] != null)
                {
                    billboardRootId = rig.billboardRoots[rootIndex].Id.Value;
                }

                int bodyIndex = FindRagdollBodyIndexFor(rig, item);
                if (bodyIndex >= 0 && rig.ragdollBodies[bodyIndex] != null)
                {
                    ragdollBodyId = rig.ragdollBodies[bodyIndex].Id.Value;
                }
            }

            if (item.kind == HierarchyItemKind.RigTarget)
            {
                return ClipObjectRef.RigTarget(item.targetId, billboardRootId, ragdollBodyId);
            }

            // The part comes off the row rather than being resolved again here. The hierarchy
            // already answered it when the row was built, and two resolutions of the same question
            // are two things that can disagree.
            //
            // The path may come back empty, and that is an address: the row for the prefab root
            // has no path below the root because it is the root.
            bool isSkinnedBone =
                previewController != null && previewController.IsSkinnedBone(item.previewIndex);
            return ClipObjectRef.Bone(
                item.displayName, item.targetId, billboardRootId, ResolveHierarchyPath(item),
                ragdollBodyId, isSkinnedBone);
        }

        /// <summary>
        /// One component: a header that folds it away, and the fields it owns -- except the
        /// object's own Transform or BoneTransform, which is never folded and never a decision
        /// anyone made, so it gets neither.
        /// </summary>
        private VisualElement BuildComponentBlock(
            ClipObjectRef objectRef, ClipComponentInstance instance)
        {
            VisualElement block = new VisualElement();
            block.AddToClassList(ComponentBlockUssClassName);

            // Everything in the animator is somewhere, so this one component is not a thing the
            // author decided to add -- there is no state in which it is missing, and folding it
            // away would just be hiding the object's own current pose. Styled apart for the same
            // reason: a foldout arrow and a header that looks exactly like Flipbook's or Socket's
            // is what made it read as one more thing someone added rather than what every part has.
            bool isIntrinsicTransform = ClipComponentModel.IsPrimaryTransform(instance.kind, objectRef);
            block.EnableInClassList(ComponentIntrinsicUssClassName, isIntrinsicTransform);

            bool isExpanded = isIntrinsicTransform || !collapsedComponentKinds.Contains(instance.kind);

            VisualElement body = new VisualElement();
            body.AddToClassList(ComponentBodyUssClassName);
            body.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            VisualElement header = new VisualElement();
            header.AddToClassList(ComponentHeaderUssClassName);

            Label title = new Label(isIntrinsicTransform
                ? DescribeComponent(objectRef, instance)
                : (isExpanded ? ExpandedGlyph : CollapsedGlyph) + DescribeComponent(objectRef, instance));
            title.AddToClassList(ComponentTitleUssClassName);
            if (!isIntrinsicTransform)
            {
                title.RegisterCallback<PointerDownEvent>(pointerEvent =>
                {
                    ToggleComponentKind(instance.kind);
                    pointerEvent.StopPropagation();
                });
            }
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

            // Only Transform and Flipbook tracks can bind a tag (Phase E target-tags spec §4.3) —
            // a bone track has no target row to look a tag up on (ClipAsset.boneTracks' own
            // remarks), and the intrinsic Transform block exists on every object whether or not a
            // track has been minted for it yet, so the button waits for one to exist.
            if ((instance.kind == ClipComponentKind.Transform || instance.kind == ClipComponentKind.Flipbook)
                && instance.HasTrack)
            {
                header.Add(BuildTagBindButton(instance));
            }

            // No remove button on the object's own transform. There is no state in which an object
            // has no transform, so a button offering to reach one would be offering something the
            // panel could not then show — and the thing it would really delete is the keys, which
            // the timeline is where you delete. A bone track stranded on a node that has since
            // become a part is not that, and removing it is the reason it is shown at all.
            if (!ClipComponentModel.IsPrimaryTransform(instance.kind, objectRef))
            {
                Button removeButton = new Button(() => ConfirmRemoveComponent(objectRef, instance));
                removeButton.text = "✕";
                removeButton.tooltip =
                    "Remove " + ClipComponentModel.DisplayName(instance.kind) + ".";
                removeButton.AddToClassList(ComponentRemoveUssClassName);
                header.Add(removeButton);
            }

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

            // Same marking, for the same reason, on the ragdoll body whose box handles are up in
            // the viewport (spec §8.3).
            if (instance.kind == ClipComponentKind.Ragdoll)
            {
                RagdollBodyDefinition ragdollBody = ResolveRagdollBody(instance);
                bool isHandleTarget = ragdollBody != null && ragdollBody.Id.Value == selectedRagdollBodyId;
                block.EnableInClassList(ComponentActiveUssClassName, isHandleTarget);
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

            // A ragdoll body has no keys at all (spec §3.3 is authored tuning, not animated data),
            // so a "0 key(s)" suffix below would read as an unkeyed track rather than what it is —
            // named rig structure, exactly the same shape Socket's own name suffix takes.
            if (instance.kind == ClipComponentKind.Ragdoll)
            {
                RagdollBodyDefinition body = ResolveRagdollBody(instance);
                if (body != null && !string.IsNullOrEmpty(body.displayName))
                {
                    return name + "  ·  " + body.displayName;
                }
                return name;
            }

            // "Not keyed" rather than "0 key(s)": for an intrinsic component the track does not
            // exist yet, and a count implies a thing there is a count of.
            if (!instance.HasTrack)
            {
                return name + "  ·  not keyed";
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
                    // By name rather than by the resolved track, because the track may not exist
                    // yet: a transform is on every object from the moment it is selected, and the
                    // first key is what mints the track to hold it.
                    AddBoneTransformFields(body, objectRef.boneName);
                    return;

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

                case ClipComponentKind.Ragdoll:
                {
                    RagdollBodyDefinition ragdollBody = ResolveRagdollBody(instance);
                    if (ragdollBody != null)
                    {
                        AddRagdollFields(body, ragdollBody);
                    }
                    return;
                }

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

        private SpriteTrack ResolveSpriteTrack(ClipComponentInstance instance)
        {
            if (selectedClip == null || selectedClip.spriteTracks == null
                || instance.index < 0 || instance.index >= selectedClip.spriteTracks.Count)
            {
                return null;
            }
            return selectedClip.spriteTracks[instance.index];
        }

        private TransformTrack ResolveTransformTrack(ClipComponentInstance instance)
        {
            if (selectedClip == null || selectedClip.transformTracks == null
                || instance.index < 0 || instance.index >= selectedClip.transformTracks.Count)
            {
                return null;
            }
            return selectedClip.transformTracks[instance.index];
        }

        // -----------------------------------------------------------------------------------
        // Track tag binding (Phase E target-tags spec §4.3, E3): a Transform or Flipbook track's
        // header button, opening the E1.5 picker to switch the track between binding by target id
        // (the object it lives under, as always) and binding by a shared role.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Builds the header button showing whether a Transform or Flipbook track binds by target id
        /// or by tag, and opening <see cref="TargetTagPicker"/> to change it.
        /// </summary>
        /// <remarks>
        /// The track keeps living under the object it was added to either way — <see cref="targetId"/>
        /// is never cleared when a tag is chosen (see <see cref="TransformTrack.tagId"/>'s remarks) —
        /// so this button only changes which id the bake actually resolves against, never where the
        /// track appears in this tree.
        /// </remarks>
        private Button BuildTagBindButton(ClipComponentInstance instance)
        {
            uint currentTagId = GetTrackTagId(instance);
            Button tagButton = new Button();
            tagButton.text = DescribeTrackTagBindingText(currentTagId);
            tagButton.clicked += () => OpenTrackTagPicker(instance, tagButton);
            tagButton.AddToClassList(ComponentBadgeUssClassName);
            tagButton.tooltip = currentTagId == 0u
                ? "This track predates tags and has none — a row's keys are stored against its "
                  + "tag. Click to assign one."
                : "Bound by tag, so this track also plays on any other rig that tags a target the "
                  + "same way (spec T2: skipped, not failed, on a rig with no such target). Click "
                  + "to move the keys to another tag.";
            return tagButton;
        }

        private uint GetTrackTagId(ClipComponentInstance instance)
        {
            if (instance.kind == ClipComponentKind.Transform)
            {
                TransformTrack track = ResolveTransformTrack(instance);
                return track != null ? track.tagId : 0u;
            }
            if (instance.kind == ClipComponentKind.Flipbook)
            {
                SpriteTrack track = ResolveSpriteTrack(instance);
                return track != null ? track.tagId : 0u;
            }
            return 0u;
        }

        private string DescribeTrackTagBindingText(uint tagId)
        {
            if (tagId == 0u)
            {
                // Legacy only (A56 D5): creation now always assigns a tag, so a tagless track is
                // an old asset asking to be fixed, and the button reads as that action.
                return "Assign tag…";
            }
            TargetTagRegistry tagRegistry = ResolveTargetTagRegistry();
            string tagName = tagRegistry != null ? tagRegistry.FindName(tagId) : null;
            return tagName != null ? "Tag: " + tagName : "Tag: (unresolved 0x" + tagId.ToString("X8") + ")";
        }

        private void OpenTrackTagPicker(ClipComponentInstance instance, Button anchor)
        {
            // rootVisualElement, not a captured "inspector root" — this is an EditorWindow, not an
            // Editor, so the whole-window element OpenComponentPicker already anchors against is the
            // one that exists here.
            TargetTagRegistry tagRegistry = ResolveTargetTagRegistry();
            VocabularyPicker.Open(
                rootVisualElement,
                anchor,
                tagRegistry,
                tagRegistry,
                // No "(none)" row (A56 D5): a keyed track has nothing legal to clear to.
                VocabularyPickerConfig.ForTrackTagRebind(tagRegistry),
                chosenTagId => ApplyTrackTagBinding(instance, chosenTagId),
                () =>
                {
                    // The registry changed underneath every open track button (a tag renamed or
                    // newly created), so the whole inspector's labels are re-derived rather than
                    // just this one's — same discipline as RigAssetEditor.RefreshAllTargetTagButtons.
                    RebuildInspector();
                });
        }

        /// <summary>
        /// One retag core for both surfaces (A56 D6): the inspector's tag button and the timeline
        /// row's tag half route through <see cref="RetagTrack"/>, merge behaviour included, so the
        /// two cannot disagree about what picking an in-use tag does.
        /// </summary>
        private void ApplyTrackTagBinding(ClipComponentInstance instance, uint chosenTagId)
        {
            if (instance.kind == ClipComponentKind.Transform)
            {
                RetagTrack(TimelineTrackKind.Transform, instance.index, chosenTagId);
            }
            else if (instance.kind == ClipComponentKind.Flipbook)
            {
                RetagTrack(TimelineTrackKind.Sprite, instance.index, chosenTagId);
            }
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

        private RagdollBodyDefinition ResolveRagdollBody(ClipComponentInstance instance)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.ragdollBodies == null
                || instance.index < 0 || instance.index >= rig.ragdollBodies.Count)
            {
                return null;
            }
            return rig.ragdollBodies[instance.index];
        }

        /// <summary>
        /// Sizes a freshly minted ragdoll body's box from its node's renderer, when it has one.
        /// </summary>
        /// <remarks>
        /// Left to the window rather than <see cref="ClipComponentModel"/> because only the preview
        /// knows the node's geometry — the model is pure over the assets and has no scene to measure
        /// against (spec §8.1). A rig-target row with no previewed node, and a previewed node with no
        /// renderer of its own, both keep <see cref="RagdollBodyDefinition"/>'s own unit-box field
        /// initializer.
        /// </remarks>
        private void SizeRagdollBoxFromRenderer(RigAsset rig, ClipObjectRef objectRef, int bodyIndex)
        {
            if (rig.ragdollBodies == null || bodyIndex < 0 || bodyIndex >= rig.ragdollBodies.Count
                || previewController == null || objectRef.kind != ClipObjectKind.Bone)
            {
                return;
            }

            int hierarchyIndex = previewController.FindHierarchyIndexByName(objectRef.boneName);
            if (hierarchyIndex < 0)
            {
                return;
            }

            Vector3 localCenter;
            Vector3 localSize;
            if (!previewController.TryGetLocalRendererBounds(
                    hierarchyIndex, out localCenter, out localSize))
            {
                return;
            }

            RagdollBodyDefinition body = rig.ragdollBodies[bodyIndex];
            body.boxCenter = ToFloat3(localCenter);
            body.boxSize = ToFloat3(localSize);
        }

        // -------------------------------------------------------------------------------------
        // Adding and removing.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The Add Component button, and the picker of what this object could carry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The transform kinds are absent: they are on every object already, so there is nothing to
        /// add. What is left is the three add-ons, and all three apply to every object — a node the
        /// rig declares no part for gets one minted when it takes a flipbook.
        /// </para>
        /// <para>
        /// Kinds that still cannot be added are listed dimmed with the reason on their hover card
        /// rather than omitted: a menu that silently leaves out the thing you came looking for reads
        /// as a bug, and the reason is usually actionable.
        /// </para>
        /// </remarks>
        private VisualElement BuildAddComponentButton(ClipObjectRef objectRef)
        {
            Button addButton = new Button();
            addButton.text = "Add Component";
            addButton.AddToClassList(AddComponentUssClassName);
            addButton.clicked += () => OpenComponentPicker(addButton, objectRef);
            return addButton;
        }

        /// <summary>Opens the picker over the whole window, so a hover card has room beside it.</summary>
        private void OpenComponentPicker(VisualElement anchor, ClipObjectRef objectRef)
        {
            componentPickerEntries.Clear();
            IReadOnlyList<ClipComponentKind> kinds = ClipComponentModel.AddableKinds;
            for (int kindIndex = 0; kindIndex < kinds.Count; kindIndex++)
            {
                ClipComponentKind kind = kinds[kindIndex];
                string unavailableReason;
                bool isAvailable = ClipComponentModel.CanAdd(
                    selectedClip, ActiveRig, objectRef, kind, out unavailableReason);

                componentPickerEntries.Add(new ClipComponentPickerEntry
                {
                    kind = kind,
                    displayName = ClipComponentModel.DisplayName(kind),
                    description = ClipComponentModel.Describe(kind),
                    isAvailable = isAvailable,
                    unavailableReason = unavailableReason
                });
            }

            ClipComponentPicker.Open(
                rootVisualElement, anchor, componentPickerEntries,
                pickedKind => AddComponent(objectRef, pickedKind));
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
                ClipComponentInstance added = ClipComponentModel.Add(
                    selectedClip, rig, objectRef, kind, DescribeNewComponentName(objectRef, kind));

                // The model has no viewport to measure against, so a freshly minted ragdoll body
                // sizes its box here — from the node's own renderer where it has one, and left at
                // the definition's unit-box default otherwise (spec §8.1).
                if (kind == ClipComponentKind.Ragdoll && added.HasTrack)
                {
                    SizeRagdollBoxFromRenderer(rig, objectRef, added.index);
                }

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

            // A part-bound component on a node the rig declares nothing for mints the part, so this
            // one clip-scoped add can write the rig too. Both undo records are opened before the
            // edit rather than the rig's being skipped when it turns out unnecessary: an undo that
            // covers half of what one click did is worse than one that covers a no-op.
            string operationName = "Add " + ClipComponentModel.DisplayName(kind);
            bool promotesNode =
                ClipComponentModel.RequiresRigTarget(kind) && !objectRef.HasRigTarget && rig != null;
            if (promotesNode)
            {
                RecordSocketEdit(rig, operationName);
            }

            RecordClipEdit(operationName);
            ClipComponentModel.Add(selectedClip, rig, objectRef, kind, string.Empty);

            // A56 D4: a track kind minted on an untagged part tags the part before the panel ever
            // shows the row, so no keyed row can exist without the tag that names it.
            if (kind == ClipComponentKind.Transform || kind == ClipComponentKind.Flipbook)
            {
                EnsureClipTrackTagsAssigned(operationName);
            }
            CommitClipEdit();

            if (promotesNode)
            {
                // Minted here rather than left to OnValidate, which does not run in time. A target
                // saved with id 0 is one no track could ever bind to.
                rig.EnsureStableIds();
                AssetDatabase.SaveAssetIfDirty(rig);
                CommitSocketEdit(true);
            }

            // A new track is a new lane, and the timeline is where its keys will be made. The
            // hierarchy is rebuilt too: a promoted node has just become a part, and its row now
            // stands for one.
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

            if (instance.kind == ClipComponentKind.Ragdoll)
            {
                ConfirmRemoveRagdoll(instance);
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

        /// <summary>Removes a ragdoll body from the rig, asking first.</summary>
        /// <remarks>
        /// No key warning to fold in — unlike a billboard root, a ragdoll body carries no clip-side
        /// data at all (spec §3.3 is authored tuning, not animated keys), so there is nothing this
        /// clip stands to lose. What the prompt still has to say is that the body is rig structure,
        /// seen by every clip that previews this rig, and that a body whose implied parent was this
        /// one becomes its own root the moment it is gone (D3's baker walks the hierarchy fresh on
        /// every bake, so nothing here has to renumber the rest of the chain by hand).
        /// </remarks>
        private void ConfirmRemoveRagdoll(ClipComponentInstance instance)
        {
            RigAsset rig = ActiveRig;
            if (rig == null)
            {
                return;
            }
            RagdollBodyDefinition body = ResolveRagdollBody(instance);
            if (body == null)
            {
                return;
            }

            string bodyLabel = string.IsNullOrEmpty(body.displayName)
                ? "this body"
                : "\"" + body.displayName + "\"";
            if (!EditorUtility.DisplayDialog(
                    "Remove Ragdoll",
                    "Remove " + bodyLabel + " from the rig's ragdoll?\n\n"
                        + "It is rig structure, so every clip in the set loses it. Any body whose "
                        + "nearest ragdolled ancestor was this one becomes its own root.",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            bool wasHandleTarget = body.Id.Value == selectedRagdollBodyId;
            RecordSocketEdit(rig, "Remove Ragdoll");
            ClipComponentModel.Remove(selectedClip, rig, instance);
            AssetDatabase.SaveAssetIfDirty(rig);
            if (wasHandleTarget)
            {
                FocusRagdollBody(0u);
            }
            CommitSocketEdit(false);

            RefreshHierarchyRows();
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
            if (socket.mode == SocketAttachMode.RigTarget)
            {
                // On the id, not the row's kind: a claimed node is the part its sockets follow.
                return item.targetId != 0u && socket.targetId == item.targetId;
            }
            return item.kind != HierarchyItemKind.RigTarget
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
            Slider blendField = new Slider("Blend Weight", 0f, 1f);
            Toggle enabledField = new Toggle("Billboarding");

            // Every field writes all three channels, read off the fields themselves. Closing over
            // the values sampled above would make each edit revert whichever sibling was changed
            // since the block was built — the block only rebuilds on the first key, so those
            // captures go stale immediately and stay stale.
            EventCallback<ChangeEvent<float>> writeFloatChannel = changeEvent =>
            {
                ApplyBillboardEdit(
                    objectRef, angleField.value, blendField.value, enabledField.value);
            };

            angleField.tooltip =
                "Degrees off the resolved facing, about the billboard frame's own up axis. Added to "
                + "the root's rest offset rather than replacing it.";
            angleField.SetValueWithoutNotify(angleOffsetDegrees);
            angleField.RegisterValueChangedCallback(writeFloatChannel);
            parent.Add(angleField);

            blendField.tooltip =
                "How much of the billboard orientation applies against the node's animated pose. "
                + "1 is fully billboarded; 0 hands the node back to its animation.";
            blendField.showInputField = true;
            blendField.SetValueWithoutNotify(blendWeight);
            blendField.RegisterValueChangedCallback(writeFloatChannel);
            parent.Add(blendField);

            enabledField.tooltip =
                "Held from its key, never eased — an enable flag is an instruction that fires at a "
                + "moment, not an approximation of anything between two moments.";
            enabledField.SetValueWithoutNotify(enabled);
            enabledField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBillboardEdit(
                    objectRef, angleField.value, blendField.value, enabledField.value);
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
            bool isFirstKey = track == null;
            if (isFirstKey)
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

            // Only the first key rebuilds the panel around the field. It is the one that changes
            // what the hint says — see AddBillboardFields — and a rebuild on every keystroke would
            // destroy the field being dragged or typed into (mirrors ApplyBoneEdit).
            if (isFirstKey)
            {
                RequestInspectorRebuild();
            }
        }

        // -------------------------------------------------------------------------------------
        // Ragdoll component body (Phase D5, spec §8.2).
        // -------------------------------------------------------------------------------------

        /// <summary>Labels for the 8 self-collision groups a ragdoll body can belong to or admit.</summary>
        private static readonly List<string> RagdollSelfCollisionGroupChoices = new List<string>
        {
            "Group 0", "Group 1", "Group 2", "Group 3",
            "Group 4", "Group 5", "Group 6", "Group 7"
        };

        /// <summary>
        /// A ragdoll body's fields: the rig-wide space it falls in, its box, its physical tuning, the
        /// joint limit for whichever space is active, and its self-collision masks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>None of this is animatable.</strong> Unlike Billboard, a ragdoll body has no key
        /// data at all (spec §3.3) — every field here is authored tuning that holds for every clip,
        /// so there is no playhead to read against and no "on key / between keys" distinction to
        /// show. That is also why every edit goes through <see cref="ApplyRagdollEdit"/> rather than
        /// a clip-recording path: there is no clip half to this component, only the rig's.
        /// </para>
        /// <para>
        /// <strong><see cref="RagdollRigSettings.space"/> is shown here but does not belong to this
        /// body.</strong> It is badged separately from the component's own "rig-wide" header badge
        /// because that header badge says this body's fields are rig structure; the space field says
        /// something stronger — that changing it changes every other body on the rig too (spec §3.2).
        /// </para>
        /// </remarks>
        private void AddRagdollFields(VisualElement parent, RagdollBodyDefinition ragdollBody)
        {
            RigAsset rig = ActiveRig;
            if (rig == null)
            {
                return;
            }

            VisualElement spaceRow = new VisualElement();
            spaceRow.style.flexDirection = FlexDirection.Row;
            spaceRow.style.alignItems = Align.Center;

            EnumField spaceField = new EnumField("Space", rig.ragdollSettings.space);
            spaceField.tooltip =
                "Planar2D falls within the billboard's own plane; Spatial3D falls freely in three "
                + "dimensions. One setting for the whole rig — every body obeys it together (spec "
                + "§3.2), which is why this changes it here rather than on this body alone.";
            spaceField.style.flexGrow = 1f;
            spaceField.RegisterValueChangedCallback(changeEvent =>
            {
                RagdollSpace newSpace = (RagdollSpace)changeEvent.newValue;
                ApplyRagdollEdit(() => { rig.ragdollSettings.space = newSpace; });
            });
            spaceRow.Add(spaceField);

            Label spaceBadge = new Label("rig-wide");
            spaceBadge.AddToClassList(ComponentBadgeUssClassName);
            spaceBadge.tooltip =
                "Comes off the rig's ragdoll settings, not this body — every body on the rig reads "
                + "the same value.";
            spaceRow.Add(spaceBadge);
            parent.Add(spaceRow);

            parent.Add(new Button(() => FocusRagdollBody(ragdollBody.Id.Value))
            {
                text = "Move in View",
                tooltip =
                    "Puts this body's box handles up in the viewport (spec §8.3) — live whether or "
                    + "not Rig Edit is on, since placing a box is a rig edit but not a hierarchy "
                    + "edit. A centre handle moves it, six face handles resize it, and a rotation "
                    + "ring turns it."
            });

            parent.Add(MakeHeading("Box"));

            Vector3Field boxCenterField = new Vector3Field("Center");
            boxCenterField.tooltip = "Local offset from the addressed node's origin.";
            boxCenterField.SetValueWithoutNotify(ToVector3(ragdollBody.boxCenter));
            boxCenterField.RegisterValueChangedCallback(changeEvent =>
            {
                float3 newCenter = ToFloat3(changeEvent.newValue);
                ApplyRagdollEdit(() => { ragdollBody.boxCenter = newCenter; });
            });
            parent.Add(boxCenterField);

            Vector3Field boxSizeField = new Vector3Field("Size");
            boxSizeField.tooltip = "Full extents, local to the addressed node. All three must be "
                + "greater than 0 (rule V-R4).";
            boxSizeField.SetValueWithoutNotify(ToVector3(ragdollBody.boxSize));
            boxSizeField.RegisterValueChangedCallback(changeEvent =>
            {
                float3 newSize = ToFloat3(changeEvent.newValue);
                ApplyRagdollEdit(() => { ragdollBody.boxSize = newSize; });
            });
            parent.Add(boxSizeField);

            Vector3Field boxRotationField = new Vector3Field("Rotation");
            boxRotationField.tooltip = "Local rotation, degrees, applied ZXY — the same convention "
                + "every typed angle in this toolkit uses.";
            boxRotationField.SetValueWithoutNotify(ToVector3(ragdollBody.boxEulerAngles));
            boxRotationField.RegisterValueChangedCallback(changeEvent =>
            {
                float3 newRotation = ToFloat3(changeEvent.newValue);
                ApplyRagdollEdit(() => { ragdollBody.boxEulerAngles = newRotation; });
            });
            parent.Add(boxRotationField);

            parent.Add(MakeHeading("Physical"));

            FloatField massField = new FloatField("Mass");
            massField.tooltip = "Must be greater than 0 (rule V-R7) — the inertia tensor is derived "
                + "from this and the box size at bake, and a zero or negative mass has no closed "
                + "form to derive it from.";
            massField.SetValueWithoutNotify(ragdollBody.mass);
            massField.RegisterValueChangedCallback(changeEvent =>
            {
                float newMass = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.mass = newMass; });
            });
            parent.Add(massField);

            FloatField linearDampingField = new FloatField("Linear Damping");
            linearDampingField.tooltip = "Per second. −1 inherits the rig's default rather than "
                + "setting one on this body.";
            linearDampingField.SetValueWithoutNotify(ragdollBody.linearDamping);
            linearDampingField.RegisterValueChangedCallback(changeEvent =>
            {
                float newLinearDamping = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.linearDamping = newLinearDamping; });
            });
            parent.Add(linearDampingField);

            FloatField angularDampingField = new FloatField("Angular Damping");
            angularDampingField.tooltip = "Per second. −1 inherits the rig's default, the same "
                + "sentinel as Linear Damping.";
            angularDampingField.SetValueWithoutNotify(ragdollBody.angularDamping);
            angularDampingField.RegisterValueChangedCallback(changeEvent =>
            {
                float newAngularDamping = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.angularDamping = newAngularDamping; });
            });
            parent.Add(angularDampingField);

            FloatField restitutionField = new FloatField("Restitution");
            restitutionField.tooltip = "Contact bounce, [0, 1].";
            restitutionField.SetValueWithoutNotify(ragdollBody.restitution);
            restitutionField.RegisterValueChangedCallback(changeEvent =>
            {
                float newRestitution = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.restitution = newRestitution; });
            });
            parent.Add(restitutionField);

            FloatField frictionField = new FloatField("Friction");
            frictionField.tooltip = "Contact friction coefficient.";
            frictionField.SetValueWithoutNotify(ragdollBody.friction);
            frictionField.RegisterValueChangedCallback(changeEvent =>
            {
                float newFriction = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.friction = newFriction; });
            });
            parent.Add(frictionField);

            AddRagdollLimitFields(parent, rig, ragdollBody);

            parent.Add(MakeHeading("Self-Collision"));

            IntegerField selfGroupField = new IntegerField("Self Group");
            selfGroupField.tooltip = "Which of 8 self-collision groups this body belongs to — a bit "
                + "index, 0-7, not a mask.";
            selfGroupField.SetValueWithoutNotify(ragdollBody.selfGroup);
            selfGroupField.RegisterValueChangedCallback(changeEvent =>
            {
                byte newSelfGroup = (byte)Mathf.Clamp(changeEvent.newValue, 0, 7);
                ApplyRagdollEdit(() => { ragdollBody.selfGroup = newSelfGroup; });
            });
            parent.Add(selfGroupField);

            MaskField selfCollidesWithField = new MaskField(
                "Collides With", RagdollSelfCollisionGroupChoices, ragdollBody.selfCollidesWith);
            selfCollidesWithField.tooltip = "Bitmask of the groups this body collides with. Both "
                + "bodies of a pair must admit each other's group for the pair to collide, and a "
                + "body's own parent-child pairs are excluded automatically regardless of this mask.";
            selfCollidesWithField.RegisterValueChangedCallback(changeEvent =>
            {
                byte newSelfCollidesWith = (byte)(changeEvent.newValue & 0xFF);
                ApplyRagdollEdit(() => { ragdollBody.selfCollidesWith = newSelfCollidesWith; });
            });
            parent.Add(selfCollidesWithField);

            Toggle collidesWithWorldField = new Toggle("Collides With World");
            collidesWithWorldField.tooltip = "Off lets this body pass through world geometry while "
                + "it still takes part in self-collision and its own joint — a cape tip, most often.";
            collidesWithWorldField.SetValueWithoutNotify(ragdollBody.collidesWithWorld);
            collidesWithWorldField.RegisterValueChangedCallback(changeEvent =>
            {
                bool newCollidesWithWorld = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.collidesWithWorld = newCollidesWithWorld; });
            });
            parent.Add(collidesWithWorldField);
        }

        /// <summary>
        /// The joint limit pair for whichever space is active — hinge range in Planar2D, swing/twist
        /// in Spatial3D.
        /// </summary>
        /// <remarks>
        /// Both pairs are always stored on <see cref="RagdollBodyDefinition"/> regardless of which
        /// one this shows (spec §3.3): switching the rig's space to look and switching back must not
        /// destroy tuning authored for the space not currently displayed.
        /// </remarks>
        private void AddRagdollLimitFields(
            VisualElement parent, RigAsset rig, RagdollBodyDefinition ragdollBody)
        {
            parent.Add(MakeHeading("Joint Limit"));
            parent.Add(MakeHint(
                "Measured against this body's implied parent — its nearest ragdolled ancestor in "
                + "the addressed hierarchy. Both the hinge pair and the swing/twist pair are always "
                + "kept, whichever one is shown here."));

            if (rig.ragdollSettings.space == RagdollSpace.Planar2D)
            {
                FloatField limitMinField = new FloatField("Hinge Min");
                limitMinField.tooltip = "Signed degrees, measured from this body's rest relative "
                    + "orientation. Must not exceed Hinge Max, both within [-180, 180] (rule V-R5).";
                limitMinField.SetValueWithoutNotify(ragdollBody.limitMinDegrees);
                limitMinField.RegisterValueChangedCallback(changeEvent =>
                {
                    float newLimitMin = changeEvent.newValue;
                    ApplyRagdollEdit(() => { ragdollBody.limitMinDegrees = newLimitMin; });
                });
                parent.Add(limitMinField);

                FloatField limitMaxField = new FloatField("Hinge Max");
                limitMaxField.tooltip = "Signed degrees. Must not be less than Hinge Min, both "
                    + "within [-180, 180] (rule V-R5).";
                limitMaxField.SetValueWithoutNotify(ragdollBody.limitMaxDegrees);
                limitMaxField.RegisterValueChangedCallback(changeEvent =>
                {
                    float newLimitMax = changeEvent.newValue;
                    ApplyRagdollEdit(() => { ragdollBody.limitMaxDegrees = newLimitMax; });
                });
                parent.Add(limitMaxField);
                return;
            }

            FloatField swingLimitField = new FloatField("Swing Limit");
            swingLimitField.tooltip = "Cone half-angle in degrees, [0, 180].";
            swingLimitField.SetValueWithoutNotify(ragdollBody.swingLimitDegrees);
            swingLimitField.RegisterValueChangedCallback(changeEvent =>
            {
                float newSwingLimit = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.swingLimitDegrees = newSwingLimit; });
            });
            parent.Add(swingLimitField);

            FloatField twistLimitField = new FloatField("Twist Limit");
            twistLimitField.tooltip = "Half-range about the joint's own axis, in degrees, [0, 180].";
            twistLimitField.SetValueWithoutNotify(ragdollBody.twistLimitDegrees);
            twistLimitField.RegisterValueChangedCallback(changeEvent =>
            {
                float newTwistLimit = changeEvent.newValue;
                ApplyRagdollEdit(() => { ragdollBody.twistLimitDegrees = newTwistLimit; });
            });
            parent.Add(twistLimitField);
        }

        private static Vector3 ToVector3(float3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static float3 ToFloat3(Vector3 value)
        {
            return new float3(value.x, value.y, value.z);
        }

        /// <summary>
        /// Writes one ragdoll field edit through a single undo record on the rig — generalising
        /// <see cref="ApplyBillboardEdit"/>'s shape past the three fields a billboard channel has to
        /// the dozen a ragdoll body carries.
        /// </summary>
        /// <remarks>
        /// A closure over the one field that changed, rather than every field passed positionally as
        /// <see cref="ApplyBillboardEdit"/> does: billboard's three values fit comfortably as
        /// parameters, and a ragdoll body's do not. What both share, and what actually matters, is
        /// that every field in the component funnels through one call site that opens exactly one
        /// undo — a run of drags on a single body coalesces the way a socket's own drag already does
        /// (<see cref="RecordSocketEdit"/>), rather than each keystroke opening its own.
        /// </remarks>
        /// <summary>
        /// Applies one ragdoll-body field edit under a single undo.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>This deliberately does not go through <c>CommitSocketEdit</c>, and that is what
        /// makes dragging a number field work.</strong> Dragging a <c>FloatField</c>'s label emits a
        /// change event on every mouse move. <c>CommitSocketEdit</c> calls <c>RebuildHierarchy</c>,
        /// so routing these edits through it tore down and rebuilt the hierarchy tree on each delta,
        /// and the rebuild took the drag's pointer capture with it — the drag died after roughly one
        /// pixel, which makes small adjustments impossible and is exactly what a drag handle is for.
        /// </para>
        /// <para>
        /// A socket edit genuinely needs that rebuild: its hierarchy row label carries the binding
        /// and the unresolved mark, so the row is stale the moment either changes. A ragdoll body's
        /// box, mass, damping and limits appear in no row label, so nothing in the tree can go stale
        /// and there is nothing to rebuild. Marking the rig dirty and the preview dirty is the whole
        /// of what this edit owes the rest of the window.
        /// </para>
        /// </remarks>
        private void ApplyRagdollEdit(System.Action mutate)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || mutate == null)
            {
                return;
            }
            RecordSocketEdit(rig, "Edit Ragdoll");
            mutate();
            EditorUtility.SetDirty(rig);
            MarkPreviewDirty();
        }
    }
}
