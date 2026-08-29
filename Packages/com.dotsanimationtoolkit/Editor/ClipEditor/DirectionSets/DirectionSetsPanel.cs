// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The 2D Direction Sets authoring pane: a clip queue over a direction set's five east-side
    /// slots, and one viewport whose facing is driven by a slider through the runtime's own resolver.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One viewer, not one per direction.</strong> The expensive step is building the preview
    /// registry, so this holds a single <see cref="ClipPreviewController"/> over a synthetic clip set
    /// containing <em>every</em> clip the open direction set names. Turning the slider is then a
    /// different clip id into <c>SamplePose</c> — no rebuild, no hitch mid-turn, and the playhead
    /// carries across the swap the way a runtime facing change does.
    /// </para>
    /// <para>
    /// <strong>The slider walks the runtime path, it does not imitate it.</strong> Angle to a
    /// facing-space vector, <see cref="FacingResolver.FromMovement"/> at the actor's direction count,
    /// <see cref="FacingResolver.Snap"/> into the set's own derived coverage, then
    /// <see cref="FacingResolver.ToAuthoredSide"/> for the east-side slot and the mirror flag. The
    /// mirror renders as a horizontal flip of the whole frame, which is mathematically what
    /// <c>PartFacing.mirrorX</c> does per part at run time — so there is no second mirror pipeline to
    /// drift.
    /// </para>
    /// <para>
    /// <strong>The camera is fixed front-on.</strong> Direction comes from the slider, never from
    /// orbiting: the game's camera does not orbit either, and a preview you can spin is a preview
    /// that can show you a pose the player will never see.
    /// </para>
    /// </remarks>
    public sealed class DirectionSetsPanel : VisualElement
    {
        private const string NoContextChoice = "— none —";

        /// <summary>
        /// The host's unit context provider, or null when the package is running alone.
        /// </summary>
        /// <remarks>
        /// Static because a host registers once from an <c>[InitializeOnLoad]</c> static constructor
        /// and the panel is built much later, on the first time the pane is opened. Read afresh each
        /// time the dropdown is built rather than captured, so a provider registered after a panel
        /// exists still shows up.
        /// </remarks>
        private static IDirectionSetContextProvider contextProvider;

        /// <summary>
        /// Registers the host's unit context provider. Null unregisters, which hides the dropdown.
        /// </summary>
        public static void SetContextProvider(IDirectionSetContextProvider provider)
        {
            contextProvider = provider;
        }

        private DirectionSetAsset directionSet;

        /// <summary>
        /// The actor direction count the slider quantizes at: the active unit context's, when one is
        /// picked, otherwise the Directions dropdown's. Distinct from the set's own coverage, which
        /// is always derived from the filled slots.
        /// </summary>
        private AnimationDirections targetDirections = AnimationDirections.Six;
        private bool hasContextDirections;
        private AnimationDirections contextDirections = AnimationDirections.Six;

        /// <summary>
        /// The <em>window's</em> preview, not one of this panel's own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Sharing is what makes the sweep free.</strong> The expensive step is building the
        /// registry, and the clips this panel queues are members of the open clip set by
        /// construction (owner directive 2026-08-29) — so they are already in the window's registry
        /// and a facing change is a different <c>clipId</c> into <c>SamplePose</c>. This panel used
        /// to own a second controller with a second <c>PreviewRenderUtility</c>, a second
        /// <c>Persistent</c> blob and a second instantiated rig, kept in step through a synthetic
        /// clip set it rebuilt whenever the queue changed. All of that was the window's registry with
        /// extra steps.
        /// </para>
        /// <para>
        /// Safe only because the tabs are exclusive: one controller cannot serve two viewports in one
        /// frame, and exactly one of them is on screen. Nothing here disposes it — the window owns
        /// its lifetime.
        /// </para>
        /// </remarks>
        private ClipPreviewController previewController;

        /// <summary>The window's clip set and rig, pushed in by the host. Never picked here.</summary>
        private ClipSetAsset sourceClipSet;
        private RigAsset previewRig;

        /// <summary>
        /// The camera angle the Clip Editor was orbited to, held while this tab has the camera
        /// head-on, and given back on the way out.
        /// </summary>
        private float restoreOrbitYaw;
        private float restoreOrbitPitch;
        private bool hasCapturedOrbit;

        /// <summary>The billboard and ragdoll state to restore when this tab hands the preview back.</summary>
        private bool restoreBillboardEnabled;

        /// <summary>Per-row warnings, keyed by the clip the row holds.</summary>
        private readonly Dictionary<ClipAsset, string> clipWarnings = new Dictionary<ClipAsset, string>();

        /// <summary>Slots the author asked for with Add Clip beyond the target's required ones.</summary>
        private readonly HashSet<Direction> extraVisibleSlots = new HashSet<Direction>();
        private readonly List<Direction> visibleSlots = new List<Direction>();

        /// <summary>
        /// The set's five slot references and its target as they were when the queue was last built.
        /// Polled each tick, which is how an inspector edit or an undo reaches this panel without any
        /// event plumbing — and it cannot miss a change the way a subscription to one of them could.
        /// </summary>
        private readonly ClipAsset[] observedSlots = new ClipAsset[5];
        private AnimationDirections observedTarget;

        private float directionAngleDegrees;
        private float playheadNormalizedTime;
        private bool isPlaying;
        private double lastTickTime;
        private Direction currentFacing = Direction.SouthEast;
        private bool isTicking;

        private ObjectField directionSetField;
        private Label sourceLabel;
        private DropdownField unitContextDropdown;
        private List<DirectionSetContextEntry> contextEntries = new List<DirectionSetContextEntry>();
        private DropdownField directionsDropdown;
        private Label coverageLabel;
        private DirectionSetClipQueueView queueView;
        private Button addClipButton;
        private Image viewportImage;
        private Label viewportStatusLabel;
        private Slider directionSlider;
        private Label directionReadoutLabel;
        private Button playToggleButton;
        private Slider scrubSlider;

        private static readonly string[] DirectionsChoices = new[] { "One", "Two", "Four", "Six", "Eight" };
        private static readonly AnimationDirections[] DirectionsValues = new[]
        {
            AnimationDirections.One, AnimationDirections.Two, AnimationDirections.Four,
            AnimationDirections.Six, AnimationDirections.Eight
        };

        public DirectionSetsPanel()
        {
            // Inline styles rather than a stylesheet, matching VatBakePanel and NewRigPanel: this
            // element carries no sheet of its own and a host's has no reason to know these rows.
            style.flexGrow = 1f;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.paddingTop = 6f;
            style.paddingBottom = 6f;

            Add(BuildToolbarRow());
            Add(BuildSourceRow());
            Add(BuildBody());
            Add(BuildTransport());

            RebuildContextDropdown();
            RebuildQueue();
        }

        /// <summary>Raised when a unit context asks for a different clip set and rig.</summary>
        /// <remarks>
        /// The panel no longer owns either, so it asks rather than assigns: the host writes its own
        /// toolbar fields, and a unit pick then runs the same load path a hand pick does.
        /// </remarks>
        public event Action<ClipSetAsset, RigAsset> SelectionRequested;

        // -----------------------------------------------------------------------------------------
        // Construction
        // -----------------------------------------------------------------------------------------

        private VisualElement BuildToolbarRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            directionSetField = new ObjectField("Direction Set")
            {
                objectType = typeof(DirectionSetAsset),
                allowSceneObjects = false
            };
            directionSetField.style.flexGrow = 1f;
            directionSetField.RegisterValueChangedCallback(changeEvent =>
            {
                LoadDirectionSet(changeEvent.newValue as DirectionSetAsset);
            });
            row.Add(directionSetField);

            unitContextDropdown = new DropdownField("Unit Context", new List<string> { NoContextChoice }, 0);
            unitContextDropdown.style.flexGrow = 1f;
            unitContextDropdown.tooltip =
                "Pick what a unit actually plays for a state. Loads the set, the rig and the actor's "
                + "turn granularity in one click.";
            unitContextDropdown.RegisterValueChangedCallback(
                changeEvent => ApplyContextEntry(unitContextDropdown.index));
            row.Add(unitContextDropdown);

            Button newSetButton = new Button(CreateNewDirectionSet) { text = "New Set" };
            row.Add(newSetButton);

            return row;
        }

        /// <summary>
        /// The read-only line saying what this pane is previewing against.
        /// </summary>
        /// <remarks>
        /// A line rather than two pickers, because the clip set and the rig are the window's and
        /// this pane only derives from them (owner directive 2026-08-29). Showing them anyway — and
        /// saying where to change them — beats a pane that silently depends on a selection it never
        /// mentions.
        /// </remarks>
        private VisualElement BuildSourceRow()
        {
            sourceLabel = new Label();
            sourceLabel.style.whiteSpace = WhiteSpace.Normal;
            sourceLabel.style.marginBottom = 4f;
            sourceLabel.style.color = new Color(0.68f, 0.68f, 0.72f);
            return sourceLabel;
        }

        private void RefreshSourceLabel()
        {
            if (sourceLabel == null)
            {
                return;
            }

            if (previewRig == null)
            {
                sourceLabel.text =
                    "No rig in the top bar — the queue and the coverage readout work without one, "
                    + "but nothing can be posed.";
                return;
            }

            sourceLabel.text =
                "Previewing " + (sourceClipSet != null ? "'" + sourceClipSet.name + "'" : "no clip set")
                + " on '" + previewRig.name + "' — the queue can only hold clips from that set.";
        }

        private VisualElement BuildBody()
        {
            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;

            VisualElement queueColumn = new VisualElement();
            queueColumn.style.width = 340f;
            queueColumn.style.marginRight = 8f;
            body.Add(queueColumn);

            directionsDropdown = new DropdownField(
                "Directions", new List<string>(DirectionsChoices), IndexOfDirections(targetDirections));
            directionsDropdown.tooltip =
                "How many directions this set is meant to end up covering. It scaffolds the queue "
                + "with the slots still to draw and sets how finely the slider steps — it never "
                + "declares coverage, which is always derived from what is actually filled.";
            directionsDropdown.RegisterValueChangedCallback(
                changeEvent => SetTargetDirections(DirectionsValues[directionsDropdown.index]));
            queueColumn.Add(directionsDropdown);

            coverageLabel = new Label();
            coverageLabel.style.whiteSpace = WhiteSpace.Normal;
            coverageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            coverageLabel.style.marginTop = 2f;
            coverageLabel.style.marginBottom = 4f;
            queueColumn.Add(coverageLabel);

            queueView = new DirectionSetClipQueueView();
            queueView.SlotAssigned += OnSlotAssigned;
            queueView.SlotMoved += OnSlotMoved;
            queueView.SlotCleared += OnSlotCleared;
            queueView.OpenClipRequested += OnOpenClipRequested;
            queueColumn.Add(queueView);

            addClipButton = new Button(AddNextUnfilledSlot) { text = "+ Add Clip" };
            queueColumn.Add(addClipButton);

            VisualElement viewerColumn = new VisualElement();
            viewerColumn.style.flexGrow = 1f;
            body.Add(viewerColumn);

            viewportStatusLabel = new Label();
            viewportStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            viewerColumn.Add(viewportStatusLabel);

            viewportImage = new Image();
            viewportImage.style.flexGrow = 1f;
            viewportImage.style.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
            viewerColumn.Add(viewportImage);

            return body;
        }

        private VisualElement BuildTransport()
        {
            VisualElement transport = new VisualElement();
            transport.style.marginTop = 6f;

            VisualElement directionRow = new VisualElement();
            directionRow.style.flexDirection = FlexDirection.Row;
            directionRow.style.alignItems = Align.Center;
            transport.Add(directionRow);

            directionRow.Add(new Label("Direction") { style = { width = 64f } });

            directionSlider = new Slider(0f, 360f) { value = directionAngleDegrees };
            directionSlider.style.flexGrow = 1f;
            directionSlider.tooltip =
                "Turn the character. 0° is due east; the readout says which facing that quantizes to "
                + "and which authored clip serves it.";
            directionSlider.RegisterValueChangedCallback(changeEvent =>
            {
                directionAngleDegrees = changeEvent.newValue;
                RefreshDirectionReadout();
            });
            directionRow.Add(directionSlider);

            directionReadoutLabel = new Label();
            directionReadoutLabel.style.width = 260f;
            directionReadoutLabel.style.whiteSpace = WhiteSpace.Normal;
            directionRow.Add(directionReadoutLabel);

            VisualElement playbackRow = new VisualElement();
            playbackRow.style.flexDirection = FlexDirection.Row;
            playbackRow.style.alignItems = Align.Center;
            transport.Add(playbackRow);

            playToggleButton = new Button(TogglePlaying) { text = "Play" };
            playToggleButton.style.width = 64f;
            playbackRow.Add(playToggleButton);

            scrubSlider = new Slider(0f, 1f) { value = playheadNormalizedTime };
            scrubSlider.style.flexGrow = 1f;
            scrubSlider.RegisterValueChangedCallback(changeEvent =>
            {
                playheadNormalizedTime = changeEvent.newValue;
            });
            playbackRow.Add(scrubSlider);

            return transport;
        }

        // -----------------------------------------------------------------------------------------
        // Host entry points
        // -----------------------------------------------------------------------------------------

        /// <summary>Opens a direction set in the panel, as a double-click on the asset does.</summary>
        public void LoadDirectionSet(DirectionSetAsset loadedSet)
        {
            directionSet = loadedSet;
            if (directionSetField != null)
            {
                directionSetField.SetValueWithoutNotify(directionSet);
            }

            extraVisibleSlots.Clear();

            // Opening a set adopts the target it was saved with rather than keeping the last one
            // looked at: the dropdown describes this set's authoring intent, not the session's.
            if (directionSet != null)
            {
                targetDirections = directionSet.targetDirections;
                if (directionsDropdown != null)
                {
                    directionsDropdown.index = IndexOfDirections(targetDirections);
                }
            }

            RebuildQueue();
        }

        /// <summary>
        /// Hands the panel the window's preview, clip set and rig. Called on every tab switch and
        /// whenever either selection changes while this pane is open.
        /// </summary>
        public void SetSource(ClipPreviewController controller, ClipSetAsset clipSet, RigAsset rig)
        {
            previewController = controller;
            sourceClipSet = clipSet;
            previewRig = rig;

            RefreshSourceLabel();
            RebuildQueue();
        }

        /// <summary>
        /// Starts or stops the per-frame tick with the pane's visibility, and borrows the shared
        /// preview's camera and billboard state for as long as it has it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Borrowed, and given back.</strong> The camera must be head-on here — direction
        /// comes from the slider, not from orbiting — but the controller is the Clip Editor's too,
        /// and silently discarding the angle an author had set up there would read as the window
        /// losing its place. <c>FrameRig</c> is no help: it sets focus and distance and does not
        /// touch the angles.
        /// </para>
        /// <para>
        /// Billboarding is forced on for the same reason it was on when this panel owned its own
        /// controller — an unbillboarded facing preview is not what the game shows — and the ragdoll
        /// is forced off, because a rig that has been dropped cannot demonstrate a facing at all.
        /// </para>
        /// </remarks>
        public void SetTicking(bool ticking)
        {
            if (ticking == isTicking)
            {
                return;
            }
            isTicking = ticking;

            if (ticking)
            {
                BorrowPreviewCamera();
                lastTickTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
                RebuildContextDropdown();
            }
            else
            {
                EditorApplication.update -= Tick;
                ReturnPreviewCamera();
            }
        }

        private void BorrowPreviewCamera()
        {
            if (previewController == null || hasCapturedOrbit)
            {
                return;
            }

            restoreOrbitYaw = previewController.OrbitYaw;
            restoreOrbitPitch = previewController.OrbitPitch;
            restoreBillboardEnabled = previewController.BillboardPreviewEnabled;
            hasCapturedOrbit = true;

            previewController.OrbitYaw = 0f;
            previewController.OrbitPitch = 0f;
            previewController.BillboardPreviewEnabled = true;
            previewController.DisableRagdollPreview();
            previewController.FrameRig();
        }

        private void ReturnPreviewCamera()
        {
            if (previewController == null || !hasCapturedOrbit)
            {
                return;
            }

            previewController.OrbitYaw = restoreOrbitYaw;
            previewController.OrbitPitch = restoreOrbitPitch;
            previewController.BillboardPreviewEnabled = restoreBillboardEnabled;
            hasCapturedOrbit = false;
        }

        // -----------------------------------------------------------------------------------------
        // Queue
        // -----------------------------------------------------------------------------------------

        private void SetTargetDirections(AnimationDirections directions)
        {
            targetDirections = directions;
            if (directionSet != null && directionSet.targetDirections != directions)
            {
                Undo.RecordObject(directionSet, "Set Target Directions");
                directionSet.targetDirections = directions;
                EditorUtility.SetDirty(directionSet);
            }
            RebuildQueue();
        }

        private void OnSlotAssigned(Direction slot, ClipAsset clip)
        {
            if (directionSet == null || directionSet.GetSlot(slot) == clip)
            {
                return;
            }
            Undo.RecordObject(directionSet, "Assign Direction Slot");
            directionSet.SetSlot(slot, clip);
            EditorUtility.SetDirty(directionSet);
            RebuildQueue();
        }

        private void OnSlotMoved(Direction fromSlot, Direction toSlot)
        {
            if (directionSet == null)
            {
                return;
            }

            ClipAsset movedClip = directionSet.GetSlot(fromSlot);
            ClipAsset displacedClip = directionSet.GetSlot(toSlot);

            Undo.RecordObject(directionSet, "Move Direction Slot");
            directionSet.SetSlot(fromSlot, null);
            directionSet.SetSlot(toSlot, movedClip);
            EditorUtility.SetDirty(directionSet);

            // Last write wins, and says so. Silently dropping the clip that was already there is the
            // one way a re-slot can lose work.
            if (displacedClip != null && displacedClip != movedClip)
            {
                Debug.LogWarning(
                    "[2D Direction Sets] '" + displacedClip.name + "' was already on the "
                        + toSlot + " slot of '" + directionSet.name + "' and has been replaced.",
                    directionSet);
            }

            // The source slot keeps its row so the move is visible rather than making a row vanish
            // from under the cursor.
            extraVisibleSlots.Add(fromSlot);
            extraVisibleSlots.Add(toSlot);
            RebuildQueue();
        }

        private void OnSlotCleared(Direction slot)
        {
            if (directionSet == null)
            {
                return;
            }
            Undo.RecordObject(directionSet, "Clear Direction Slot");
            directionSet.SetSlot(slot, null);
            EditorUtility.SetDirty(directionSet);
            extraVisibleSlots.Remove(slot);
            RebuildQueue();
        }

        private void OnOpenClipRequested(ClipAsset clip)
        {
            if (clip == null)
            {
                return;
            }
            ClipEditorWindow.FocusClipEditing();
            EditorGUIUtility.PingObject(clip);
            Selection.activeObject = clip;
        }

        /// <summary>
        /// Adds a row for the next slot in promotion order that has neither a clip nor a row yet.
        /// </summary>
        /// <remarks>
        /// It adds a <em>row</em>, not a clip: the required slots for the current target are already
        /// on screen as empty placeholders, so what this is for is reaching a slot beyond them — the
        /// East profile on a Six target, say. With all five showing there is nothing left to add and
        /// the button says so by being disabled.
        /// </remarks>
        private void AddNextUnfilledSlot()
        {
            for (int slotIndex = 0; slotIndex < DirectionSetClipQueueView.SlotOrder.Length; slotIndex++)
            {
                Direction slot = DirectionSetClipQueueView.SlotOrder[slotIndex];
                if (!visibleSlots.Contains(slot))
                {
                    extraVisibleSlots.Add(slot);
                    RebuildQueue();
                    return;
                }
            }
        }

        private void RebuildQueue()
        {
            RecomputeVisibleSlots();
            RefreshClipWarnings();

            if (queueView != null)
            {
                queueView.Rebuild(directionSet, visibleSlots, clipWarnings);
            }
            if (addClipButton != null)
            {
                addClipButton.SetEnabled(
                    directionSet != null
                        && visibleSlots.Count < DirectionSetClipQueueView.SlotOrder.Length);
            }

            RefreshCoverageLabel();
            CaptureObservedState();
            RefreshDirectionReadout();
        }

        private void RecomputeVisibleSlots()
        {
            visibleSlots.Clear();
            if (directionSet == null)
            {
                return;
            }

            Direction[] requiredSlots = DirectionSetAsset.GetRequiredSlots(targetDirections);
            for (int slotIndex = 0; slotIndex < DirectionSetClipQueueView.SlotOrder.Length; slotIndex++)
            {
                Direction slot = DirectionSetClipQueueView.SlotOrder[slotIndex];
                bool isRequired = Array.IndexOf(requiredSlots, slot) >= 0;
                if (isRequired || directionSet.GetSlot(slot) != null || extraVisibleSlots.Contains(slot))
                {
                    visibleSlots.Add(slot);
                }
            }
        }

        private void RefreshCoverageLabel()
        {
            if (coverageLabel == null)
            {
                return;
            }

            if (directionSet == null)
            {
                coverageLabel.text = string.Empty;
                coverageLabel.style.color = new StyleColor(StyleKeyword.Null);
                return;
            }

            bool isValidFill = directionSet.TryGetEffectiveDirections(
                out AnimationDirections effectiveDirections);

            if (!isValidFill)
            {
                // Word for word what DirectionSetBakeUtil-style bake warnings say, because the two
                // read the same method: the panel cannot describe a pattern the bake would judge
                // differently.
                coverageLabel.text =
                    "Coverage: " + effectiveDirections + " — invalid fill pattern, rounded down. "
                    + "Fill exactly one of: SouthEast only (Two), +NorthEast (Four), +South+North "
                    + "(Six), all five (Eight), or South only (One).";
                coverageLabel.style.color = new StyleColor(new Color(1f, 0.55f, 0.2f));
                return;
            }

            List<string> missingNames = new List<string>();
            Direction[] requiredSlots = DirectionSetAsset.GetRequiredSlots(targetDirections);
            for (int slotIndex = 0; slotIndex < requiredSlots.Length; slotIndex++)
            {
                if (directionSet.GetSlot(requiredSlots[slotIndex]) == null)
                {
                    missingNames.Add(DirectionSetClipQueueView.ShortName(requiredSlots[slotIndex]));
                }
            }

            if (missingNames.Count == 0)
            {
                coverageLabel.text = "Coverage: " + effectiveDirections;
                coverageLabel.style.color = new StyleColor(StyleKeyword.Null);
                return;
            }

            coverageLabel.text =
                "Coverage: " + effectiveDirections + " — missing: " + string.Join(", ", missingNames);
            coverageLabel.style.color = new StyleColor(new Color(0.95f, 0.8f, 0.35f));
        }

        // -----------------------------------------------------------------------------------------
        // Preview
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Marks any queued clip the window's registry does not hold.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The question narrowed when the preview became shared.</strong> This used to run
        /// every queued clip through <c>ClipValidation.ValidateBind</c> against a rig of its own, to
        /// keep one clip authored for another rig out of a registry it would have failed to build.
        /// The window's own validation badge already asks that question of the open set, and the
        /// clips here are members of that set — so the one thing a row can still be wrong about is
        /// not being in it.
        /// </para>
        /// <para>
        /// Answered by asking the registry rather than by re-deriving membership from the set's
        /// list: <c>SamplePose</c> returning false is the same answer the viewer acts on, so a row
        /// cannot be marked healthy while the viewport shows nothing for it.
        /// </para>
        /// </remarks>
        private void RefreshClipWarnings()
        {
            clipWarnings.Clear();
            if (directionSet == null || previewController == null || !previewController.HasRegistry)
            {
                return;
            }

            List<ClipAsset> queuedClips = CollectQueuedClips();
            for (int clipIndex = 0; clipIndex < queuedClips.Count; clipIndex++)
            {
                ClipAsset queuedClip = queuedClips[clipIndex];
                if (!previewController.IsClipInRegistry(queuedClip.Id.Value))
                {
                    clipWarnings[queuedClip] = "Not in '"
                        + (sourceClipSet != null ? sourceClipSet.name : "the open clip set")
                        + "' — it cannot be previewed here. Add it to that set, or open the set that "
                        + "holds it in the top bar.";
                }
            }
        }

        private List<ClipAsset> CollectQueuedClips()
        {
            List<ClipAsset> queuedClips = new List<ClipAsset>();
            if (directionSet == null)
            {
                return queuedClips;
            }
            for (int slotIndex = 0; slotIndex < DirectionSetClipQueueView.SlotOrder.Length; slotIndex++)
            {
                ClipAsset slotClip = directionSet.GetSlot(DirectionSetClipQueueView.SlotOrder[slotIndex]);
                if (slotClip != null && !queuedClips.Contains(slotClip))
                {
                    queuedClips.Add(slotClip);
                }
            }
            return queuedClips;
        }

        // -----------------------------------------------------------------------------------------
        // Unit context
        // -----------------------------------------------------------------------------------------

        private void RebuildContextDropdown()
        {
            if (unitContextDropdown == null)
            {
                return;
            }

            contextEntries.Clear();
            if (contextProvider != null)
            {
                IReadOnlyList<DirectionSetContextEntry> hostEntries = contextProvider.GetEntries();
                for (int entryIndex = 0; hostEntries != null && entryIndex < hostEntries.Count; entryIndex++)
                {
                    contextEntries.Add(hostEntries[entryIndex]);
                }
            }

            unitContextDropdown.style.display = contextProvider == null
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            List<string> choices = new List<string> { NoContextChoice };
            for (int entryIndex = 0; entryIndex < contextEntries.Count; entryIndex++)
            {
                DirectionSetContextEntry entry = contextEntries[entryIndex];
                choices.Add(entry.set != null ? entry.label : entry.label + "  (unassigned)");
            }
            unitContextDropdown.choices = choices;
            unitContextDropdown.SetValueWithoutNotify(choices[0]);
        }

        private void ApplyContextEntry(int choiceIndex)
        {
            // Index 0 is the "none" row: back to a hand-picked set and rig, and the Directions
            // dropdown driving the quantize again.
            if (choiceIndex <= 0 || choiceIndex > contextEntries.Count)
            {
                hasContextDirections = false;
                RefreshDirectionReadout();
                return;
            }

            DirectionSetContextEntry entry = contextEntries[choiceIndex - 1];
            hasContextDirections = true;
            contextDirections = entry.actorDirections;

            // Asked for, not assigned: the clip set and the rig belong to the window, and routing
            // the request through it is what makes a unit pick and a hand pick the same load.
            if (entry.previewRig != null || entry.previewClipSet != null)
            {
                SelectionRequested?.Invoke(entry.previewClipSet, entry.previewRig);
            }
            if (entry.set != null)
            {
                LoadDirectionSet(entry.set);
            }
            else
            {
                // Nothing to load, and saying so beats leaving the previous set on screen looking
                // like the answer.
                viewportStatusLabel.text =
                    "'" + entry.label + "' has no direction set assigned yet — assign one on the unit.";
            }
            RefreshDirectionReadout();
        }

        // -----------------------------------------------------------------------------------------
        // Viewer
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The direction count the slider quantizes at — the actor's when a unit context is active,
        /// the Directions dropdown's otherwise.
        /// </summary>
        private AnimationDirections QuantizeDirections
        {
            get { return hasContextDirections ? contextDirections : targetDirections; }
        }

        /// <summary>
        /// Walks the runtime facing path for the current slider angle.
        /// </summary>
        /// <param name="memberFacing">The facing the actor's direction count quantizes to.</param>
        /// <param name="clipFacing">The east-side slot that ends up serving it.</param>
        /// <param name="mirrorX">Whether that slot's clip has to be mirrored to serve it.</param>
        /// <param name="foldedFacing">
        /// What the set's own coverage folds <paramref name="memberFacing"/> onto — equal to it when
        /// the set covers everything the actor turns through, and the visible degradation when it
        /// does not.
        /// </param>
        private void ResolveCurrentFacing(
            out Direction memberFacing,
            out Direction foldedFacing,
            out Direction clipFacing,
            out bool mirrorX)
        {
            float angleRadians = Mathf.Deg2Rad * directionAngleDegrees;
            float2 facingVector = new float2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));

            memberFacing = FacingResolver.FromMovement(in facingVector, QuantizeDirections, currentFacing);
            currentFacing = memberFacing;

            AnimationDirections coverage = AnimationDirections.One;
            if (directionSet != null)
            {
                directionSet.TryGetEffectiveDirections(out coverage);
            }

            foldedFacing = FacingResolver.Snap(memberFacing, coverage);
            FacingResolver.ToAuthoredSide(foldedFacing, out clipFacing, out mirrorX);
        }

        private void RefreshDirectionReadout()
        {
            if (directionReadoutLabel == null)
            {
                return;
            }

            Direction memberFacing;
            Direction foldedFacing;
            Direction clipFacing;
            bool mirrorX;
            ResolveCurrentFacing(out memberFacing, out foldedFacing, out clipFacing, out mirrorX);

            string readout = memberFacing.ToString();
            if (foldedFacing != memberFacing)
            {
                AnimationDirections coverage = AnimationDirections.One;
                if (directionSet != null)
                {
                    directionSet.TryGetEffectiveDirections(out coverage);
                }
                readout += " → " + foldedFacing + " (set covers " + coverage + ")";
            }
            if (mirrorX)
            {
                readout += " — mirrors " + clipFacing;
            }
            directionReadoutLabel.text = readout;

            // The mirror is a flip of the whole rendered frame, which is exactly what negating every
            // part's local x does at run time — no second mirror pipeline to disagree with the game.
            if (viewportImage != null)
            {
                viewportImage.style.scale = new StyleScale(
                    new Scale(new Vector2(mirrorX ? -1f : 1f, 1f)));
            }
        }

        private void TogglePlaying()
        {
            isPlaying = !isPlaying;
            playToggleButton.text = isPlaying ? "Pause" : "Play";
            lastTickTime = EditorApplication.timeSinceStartup;
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            double elapsed = now - lastTickTime;
            lastTickTime = now;

            if (previewController == null)
            {
                return;
            }

            // An inspector edit or an undo changed the asset under us. Polled rather than subscribed
            // because every route into the asset — the inspector, an undo, another tool — lands here,
            // and five reference comparisons a frame is cheaper than being wrong.
            if (HasObservedStateChanged())
            {
                if (directionSet != null)
                {
                    targetDirections = directionSet.targetDirections;
                    if (directionsDropdown != null)
                    {
                        directionsDropdown.SetValueWithoutNotify(
                            DirectionsChoices[IndexOfDirections(targetDirections)]);
                    }
                }
                RebuildQueue();
            }

            Direction memberFacing;
            Direction foldedFacing;
            Direction clipFacing;
            bool mirrorX;
            ResolveCurrentFacing(out memberFacing, out foldedFacing, out clipFacing, out mirrorX);

            ClipAsset facingClip = directionSet != null ? directionSet.GetSlot(clipFacing) : null;

            if (isPlaying && facingClip != null)
            {
                // True clip speed, so two clips of different lengths in one set play at the rates
                // they will run at — sweeping the slider mid-play is the mismatched-foot-phase check
                // this viewer exists for.
                float duration = Mathf.Max(ClipAsset.MinimumDuration, facingClip.duration);
                float advanced = playheadNormalizedTime + (float)elapsed / duration;
                playheadNormalizedTime = advanced - Mathf.Floor(advanced);
                if (scrubSlider != null)
                {
                    scrubSlider.SetValueWithoutNotify(playheadNormalizedTime);
                }
            }

            string status = previewController.StatusMessage;
            if (previewRig == null)
            {
                // In this pane's own words: the controller points at the Clip Editor's Rig field,
                // which is on a tab the author is not looking at.
                status = "No rig in the top bar — the queue and the coverage readout work without "
                    + "one, but nothing can be posed.";
            }
            else if (facingClip == null && string.IsNullOrEmpty(status))
            {
                status = directionSet == null
                    ? "Assign a Direction Set to preview it."
                    : "Nothing authored for " + clipFacing + " — queue a clip for that slot.";
            }
            else if (facingClip != null && previewController.HasRegistry)
            {
                // The playhead carries across the swap: a facing change at run time keeps normalized
                // time too, and a preview that restarted the clip would hide every foot-phase
                // mismatch this viewer is for.
                if (!previewController.SamplePose(facingClip.Id.Value, playheadNormalizedTime))
                {
                    status = "'" + facingClip.name + "' is not in '"
                        + (sourceClipSet != null ? sourceClipSet.name : "the open clip set")
                        + "' — add it there to preview it.";
                }
            }

            if (viewportStatusLabel != null)
            {
                viewportStatusLabel.text = status;
            }

            if (viewportImage == null)
            {
                return;
            }
            Rect viewportRect = viewportImage.contentRect;
            if (float.IsNaN(viewportRect.width) || viewportRect.width < 1f || viewportRect.height < 1f)
            {
                // Layout has not run yet; rendering into a zero rect throws inside the utility.
                return;
            }

            Texture renderedTexture = previewController.Render(
                Mathf.RoundToInt(viewportRect.width), Mathf.RoundToInt(viewportRect.height));
            if (renderedTexture != null)
            {
                viewportImage.image = renderedTexture;
                viewportImage.MarkDirtyRepaint();
            }
        }

        private bool HasObservedStateChanged()
        {
            if (directionSet == null)
            {
                return false;
            }
            if (directionSet.targetDirections != observedTarget)
            {
                return true;
            }
            for (int slotIndex = 0; slotIndex < DirectionSetClipQueueView.SlotOrder.Length; slotIndex++)
            {
                if (directionSet.GetSlot(DirectionSetClipQueueView.SlotOrder[slotIndex])
                    != observedSlots[slotIndex])
                {
                    return true;
                }
            }
            return false;
        }

        private void CaptureObservedState()
        {
            for (int slotIndex = 0; slotIndex < DirectionSetClipQueueView.SlotOrder.Length; slotIndex++)
            {
                observedSlots[slotIndex] = directionSet != null
                    ? directionSet.GetSlot(DirectionSetClipQueueView.SlotOrder[slotIndex])
                    : null;
            }
            observedTarget = directionSet != null ? directionSet.targetDirections : targetDirections;
        }

        // -----------------------------------------------------------------------------------------
        // New Set
        // -----------------------------------------------------------------------------------------

        private void CreateNewDirectionSet()
        {
            string savePath = EditorUtility.SaveFilePanelInProject(
                "New Direction Set", "NewDirectionSet", "asset",
                "Where should the new direction set live?");
            if (string.IsNullOrEmpty(savePath))
            {
                return;
            }

            DirectionSetAsset createdSet = ScriptableObject.CreateInstance<DirectionSetAsset>();

            // Six is the roster default: both three-quarter pairs plus head-on and head-away, which
            // is what a character that has to read while walking toward and away from the camera
            // needs. Anything less is a deliberate narrowing, so it should be chosen rather than
            // inherited.
            createdSet.targetDirections = AnimationDirections.Six;

            AssetDatabase.CreateAsset(createdSet, savePath);
            AssetDatabase.SaveAssets();

            if (directionSetField != null)
            {
                directionSetField.value = createdSet;
                return;
            }
            LoadDirectionSet(createdSet);
        }

        private static int IndexOfDirections(AnimationDirections directions)
        {
            for (int index = 0; index < DirectionsValues.Length; index++)
            {
                if (DirectionsValues[index] == directions)
                {
                    return index;
                }
            }
            return 3;
        }
    }
}
