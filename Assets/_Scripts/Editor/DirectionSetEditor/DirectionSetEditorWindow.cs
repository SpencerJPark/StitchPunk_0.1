#if UNITY_EDITOR
using System.Collections.Generic;
using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// ===========================================================================
//  Direction Set Editor  (DirectionFacing_System.md §6a)
//  Open via: Window > Stitch Punk > Direction Set Editor, or double-click a DirectionSetSO,
//  or the "Direction Sets" button in the DOTS Animation Toolkit Clip Editor's own toolbar.
//
//  Opens a DirectionSetSO and shows one live preview pane per direction of its DERIVED coverage
//  (filling northEast promotes SouthEast-only from Two to Four, etc. — see
//  DirectionSetSO.TryGetEffectiveDirections). West-side panes are the same clip as their east-side
//  pair, rendered as a horizontal mirror of the whole frame — visually identical to what
//  PartFacing.mirrorX does per part at runtime, without a second mirror pipeline.
//
//  Reuses the toolkit's own ClipPreviewController (one instance per pane) rather than building a
//  second preview pipeline: it is the same "transient registry blob in, rendered texture out"
//  machinery the Clip Editor's own viewport uses, so a pose shown here is guaranteed to match what
//  the game renders. Clip CONTENT editing, PartFacing view offsets, and rig editing are explicitly
//  out of scope for this tool — "Open in Clip Editor" links out for the first.
// ===========================================================================
[InitializeOnLoad]
public class DirectionSetEditorWindow : EditorWindow
{
    private const int PreviewSize = 160;

    private DirectionSetSO directionSet;
    private RigAsset previewRig;
    private float playheadTime;

    private ObjectField directionSetField;
    private Label coverageLabel;
    private Label rigHintLabel;
    private VisualElement paneContainer;

    private readonly Dictionary<Direction, DirectionPreviewPane> panes = new Dictionary<Direction, DirectionPreviewPane>();

    // [InitializeOnLoad] is load-bearing here, not decoration: a bare static constructor only runs
    // the first time something touches this type, which without it never happened until a user
    // manually opened this window — so the Clip Editor's toolbar button (which only shows once this
    // subscribes) never appeared on a fresh domain load. [InitializeOnLoad] forces the static ctor
    // to run at editor load / every domain reload, before any window is opened.
    static DirectionSetEditorWindow()
    {
        ClipEditorWindow.OnDirectionSetsButtonClicked += OpenWindow;
    }

    [MenuItem("Window/Stitch Punk/Direction Set Editor")]
    public static void OpenWindow()
    {
        DirectionSetEditorWindow window = GetWindow<DirectionSetEditorWindow>("Direction Sets");
        window.minSize = new Vector2(640f, 420f);
    }

    [OnOpenAsset]
    public static bool OnOpenDirectionSetAsset(int instanceId, int line)
    {
        Object openedAsset = EditorUtility.EntityIdToObject(instanceId);
        if (openedAsset is DirectionSetSO openedDirectionSet)
        {
            DirectionSetEditorWindow window = GetWindow<DirectionSetEditorWindow>("Direction Sets");
            window.minSize = new Vector2(640f, 420f);
            window.LoadDirectionSet(openedDirectionSet);
            return true;
        }
        return false;
    }

    private void LoadDirectionSet(DirectionSetSO loadedDirectionSet)
    {
        directionSet = loadedDirectionSet;
        directionSetField?.SetValueWithoutNotify(directionSet);
        RebuildPanes();
    }

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        root.style.paddingTop = 6;
        root.style.paddingBottom = 6;

        VisualElement toolbarRow = new VisualElement();
        toolbarRow.style.flexDirection = FlexDirection.Row;
        toolbarRow.style.marginBottom = 4;
        root.Add(toolbarRow);

        directionSetField = new ObjectField("Direction Set") { objectType = typeof(DirectionSetSO), value = directionSet };
        directionSetField.style.flexGrow = 1;
        directionSetField.RegisterValueChangedCallback(changeEvent =>
        {
            directionSet = changeEvent.newValue as DirectionSetSO;
            RebuildPanes();
        });
        toolbarRow.Add(directionSetField);

        ObjectField rigField = new ObjectField("Preview Rig") { objectType = typeof(RigAsset), value = previewRig };
        rigField.style.flexGrow = 1;
        rigField.RegisterValueChangedCallback(changeEvent =>
        {
            previewRig = changeEvent.newValue as RigAsset;
            foreach (DirectionPreviewPane pane in panes.Values)
            {
                pane.Controller.SetRig(previewRig);
            }
            rigHintLabel.style.display = previewRig == null ? DisplayStyle.Flex : DisplayStyle.None;
        });
        toolbarRow.Add(rigField);

        coverageLabel = new Label();
        coverageLabel.style.marginBottom = 4;
        coverageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        root.Add(coverageLabel);

        rigHintLabel = new Label("Assign a Preview Rig above to pose these clips — the coverage " +
                                  "readout and slot pickers work without one.");
        rigHintLabel.style.marginBottom = 4;
        rigHintLabel.style.whiteSpace = WhiteSpace.Normal;
        root.Add(rigHintLabel);

        paneContainer = new VisualElement();
        paneContainer.style.flexDirection = FlexDirection.Row;
        paneContainer.style.flexWrap = Wrap.Wrap;
        paneContainer.style.flexGrow = 1;
        root.Add(paneContainer);

        VisualElement scrubRow = new VisualElement();
        scrubRow.style.flexDirection = FlexDirection.Row;
        scrubRow.style.marginTop = 4;
        root.Add(scrubRow);

        scrubRow.Add(new Label("Scrub") { style = { width = 60 } });
        Slider scrubSlider = new Slider(0f, 1f) { value = playheadTime };
        scrubSlider.style.flexGrow = 1;
        scrubSlider.RegisterValueChangedCallback(changeEvent => playheadTime = changeEvent.newValue);
        scrubRow.Add(scrubSlider);

        RebuildPanes();
    }

    private void OnEnable()
    {
        EditorApplication.update += Tick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        DisposePanes();
    }

    private void RebuildPanes()
    {
        if (paneContainer == null)
            return;

        DisposePanes();
        paneContainer.Clear();

        if (directionSet == null)
        {
            coverageLabel.text = "Assign a Direction Set above to preview it.";
            coverageLabel.style.color = new StyleColor(StyleKeyword.Null);
            return;
        }

        bool isValidFill = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);
        coverageLabel.text = isValidFill
            ? $"Coverage: {effectiveDirections}"
            : $"Coverage: {effectiveDirections} — invalid fill pattern, rounded down. Fill exactly " +
              "one of: SouthEast only (Two), +NorthEast (Four), +South+North (Six), all five (Eight), " +
              "or South only (One).";
        coverageLabel.style.color = isValidFill
            ? new StyleColor(StyleKeyword.Null)
            : new StyleColor(new Color(1f, 0.55f, 0.2f));

        foreach (Direction member in GetMembers(effectiveDirections))
        {
            FacingResolver.ToAuthoredSide(member, out Direction clipFacing, out bool mirrorX);
            DirectionPreviewPane pane = BuildPane(member, clipFacing, mirrorX);
            panes[member] = pane;
            paneContainer.Add(pane.Root);
        }
    }

    private DirectionPreviewPane BuildPane(Direction member, Direction clipFacing, bool mirrorX)
    {
        VisualElement paneRoot = new VisualElement();
        paneRoot.style.width = PreviewSize + 12;
        paneRoot.style.marginRight = 6;
        paneRoot.style.marginBottom = 6;
        paneRoot.style.alignItems = Align.Center;

        Label titleLabel = new Label(member.ToString());
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        paneRoot.Add(titleLabel);

        Image previewImage = new Image();
        previewImage.style.width = PreviewSize;
        previewImage.style.height = PreviewSize;
        previewImage.style.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
        if (mirrorX)
        {
            // The whole rendered frame, flipped — mathematically identical to negating every part's
            // local x the way PartFacing.mirrorX does at runtime, without a second render pipeline.
            previewImage.style.scale = new StyleScale(new Scale(new Vector2(-1f, 1f)));
        }
        paneRoot.Add(previewImage);

        ClipPreviewController controller = new ClipPreviewController();
        controller.SetRig(previewRig);

        ClipSetAsset syntheticSet = ScriptableObject.CreateInstance<ClipSetAsset>();
        syntheticSet.hideFlags = HideFlags.HideAndDontSave;

        DirectionPreviewPane pane = new DirectionPreviewPane
        {
            Root = paneRoot,
            Image = previewImage,
            Controller = controller,
            SyntheticSet = syntheticSet,
        };

        if (mirrorX)
        {
            Label mirrorLabel = new Label($"Mirrors {clipFacing}");
            mirrorLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            paneRoot.Add(mirrorLabel);
        }
        else
        {
            ObjectField slotField = new ObjectField { objectType = typeof(ClipAsset), value = directionSet.GetSlot(clipFacing) };
            slotField.style.width = PreviewSize;
            slotField.RegisterValueChangedCallback(changeEvent =>
            {
                SetDirectionSetSlot(clipFacing, changeEvent.newValue as ClipAsset);
                RebuildPanes();
            });
            paneRoot.Add(slotField);

            Button openInClipEditorButton = new Button(() => OpenClipInClipEditor(directionSet.GetSlot(clipFacing)))
            {
                text = "Open in Clip Editor"
            };
            paneRoot.Add(openInClipEditorButton);
        }

        pane.SetClip(directionSet.GetSlot(clipFacing));
        return pane;
    }

    private void SetDirectionSetSlot(Direction eastSideFacing, ClipAsset clip)
    {
        if (directionSet == null)
            return;

        Undo.RecordObject(directionSet, "Set Direction Slot");
        switch (eastSideFacing)
        {
            case Direction.SouthEast: directionSet.southEast = clip; break;
            case Direction.NorthEast: directionSet.northEast = clip; break;
            case Direction.South: directionSet.south = clip; break;
            case Direction.North: directionSet.north = clip; break;
            case Direction.East: directionSet.east = clip; break;
        }
        EditorUtility.SetDirty(directionSet);
    }

    private static void OpenClipInClipEditor(ClipAsset clip)
    {
        ClipEditorWindow.ShowWindow();
        if (clip != null)
        {
            EditorGUIUtility.PingObject(clip);
        }
    }

    private void Tick()
    {
        if (directionSet == null || panes.Count == 0)
            return;

        foreach (DirectionPreviewPane pane in panes.Values)
        {
            if (pane.BoundClip == null || !pane.Controller.HasRegistry)
                continue;

            pane.Controller.SamplePose(pane.BoundClip.Id.Value, playheadTime);
            Texture rendered = pane.Controller.Render(PreviewSize, PreviewSize);
            if (rendered != null)
            {
                pane.Image.image = rendered;
                pane.Image.MarkDirtyRepaint();
            }
        }
    }

    private void DisposePanes()
    {
        foreach (DirectionPreviewPane pane in panes.Values)
        {
            pane.Dispose();
        }
        panes.Clear();
    }

    // Matches AnimationDirections' own member tables (DirectionEnums.cs) — south facing the camera,
    // every level from Two upward adding one mirror-symmetric pair to the one below it.
    private static Direction[] GetMembers(AnimationDirections directions)
    {
        switch (directions)
        {
            case AnimationDirections.One:
                return new[] { Direction.South };
            case AnimationDirections.Two:
                return new[] { Direction.SouthEast, Direction.SouthWest };
            case AnimationDirections.Four:
                return new[] { Direction.SouthEast, Direction.NorthEast, Direction.NorthWest, Direction.SouthWest };
            case AnimationDirections.Six:
                return new[]
                {
                    Direction.South, Direction.SouthEast, Direction.NorthEast,
                    Direction.North, Direction.NorthWest, Direction.SouthWest
                };
            default:
                return new[]
                {
                    Direction.North, Direction.NorthEast, Direction.East, Direction.SouthEast,
                    Direction.South, Direction.SouthWest, Direction.West, Direction.NorthWest
                };
        }
    }

    private sealed class DirectionPreviewPane
    {
        public VisualElement Root;
        public Image Image;
        public ClipPreviewController Controller;
        public ClipSetAsset SyntheticSet;
        public ClipAsset BoundClip { get; private set; }

        public void SetClip(ClipAsset clip)
        {
            if (BoundClip == clip)
                return;

            BoundClip = clip;
            SyntheticSet.clips.Clear();
            if (clip != null)
            {
                SyntheticSet.clips.Add(clip);
            }
            Controller.SetClipSet(SyntheticSet);
        }

        public void Dispose()
        {
            Controller.Dispose();
            Object.DestroyImmediate(SyntheticSet);
        }
    }
}
#endif
