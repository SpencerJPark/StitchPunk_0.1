// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The Actor Editor tab: a profile picker over three columns — layers, the composited
    /// viewport, and an inspector. Layer authoring and the composited pose land in later tasks.
    /// </summary>
    public sealed class ActorEditorPanel : VisualElement
    {
        private const string LayersColumnUssClassName = "actor-editor__layers-column";
        private const string ViewportColumnUssClassName = "actor-editor__viewport-column";
        private const string InspectorColumnUssClassName = "actor-editor__inspector-column";

        private const float SideColumnWidth = 340f;

        /// <summary>Raised when the header's Profile field picks a different asset.</summary>
        public event Action<ActorProfileAsset> ProfileChanged;

        private ActorProfileAsset profile;

        // The window's preview, not one owned here: the clips a profile plays will already be in
        // the window's registry, so a facing or layer change is just a different sample into it.
        // Safe only because the tabs are exclusive; nothing here disposes it.
        private ClipPreviewController previewController;
        private RigAsset windowRig;

        private float restoreOrbitYaw;
        private float restoreOrbitPitch;
        private bool hasCapturedOrbit;
        private bool restoreBillboardEnabled;
        private bool isTicking;

        private ObjectField profileField;
        private Label validationBadgeLabel;
        private Label transportPlaceholderLabel;
        private VisualElement layersColumn;
        private VisualElement viewportColumn;
        private VisualElement inspectorColumn;
        private Image viewportImage;
        private Label viewportStatusLabel;

        public ActorEditorPanel()
        {
            // Inline styles rather than a stylesheet, matching DirectionSetsPanel/VatBakePanel:
            // this element carries no sheet of its own.
            style.flexGrow = 1f;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.paddingTop = 6f;
            style.paddingBottom = 6f;

            Add(BuildHeaderRow());
            Add(BuildBody());
        }

        /// <summary>The profile this panel is authoring. Raises <see cref="ProfileChanged"/> whenever it actually changes, whether set here or picked through the header field.</summary>
        public ActorProfileAsset Profile
        {
            get { return profile; }
            set
            {
                if (profile == value)
                {
                    return;
                }
                profile = value;
                if (profileField != null)
                {
                    profileField.SetValueWithoutNotify(profile);
                }
                ProfileChanged?.Invoke(profile);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Host entry points
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Hands the panel the window's preview and rig. Called on every tab switch and whenever
        /// the toolbar Rig field changes while this pane is open.
        /// </summary>
        public void SetSource(ClipPreviewController controller, RigAsset rig)
        {
            previewController = controller;
            windowRig = rig;
        }

        // Starts or stops the per-frame tick with the pane's visibility, and borrows the shared
        // preview's camera and billboard state for as long as it has it, restoring both on the way
        // out, the same contract DirectionSetsPanel used for its own tab.
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
                EditorApplication.update += Tick;
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
        // Layout
        // -----------------------------------------------------------------------------------------

        private VisualElement BuildHeaderRow()
        {
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6f;

            profileField = new ObjectField("Profile") { objectType = typeof(ActorProfileAsset) };
            profileField.name = "actor-editor-profile-field";
            profileField.style.flexGrow = 1f;
            profileField.RegisterValueChangedCallback(
                changeEvent => Profile = changeEvent.newValue as ActorProfileAsset);
            header.Add(profileField);

            // Filled in later with the profile's validation status.
            validationBadgeLabel = new Label(string.Empty) { name = "actor-editor-validation-badge" };
            validationBadgeLabel.style.marginLeft = 6f;
            header.Add(validationBadgeLabel);

            // Filled in later with Reset, the transport and the direction slider.
            transportPlaceholderLabel = new Label(string.Empty) { name = "actor-editor-transport-row" };
            transportPlaceholderLabel.style.marginLeft = 6f;
            header.Add(transportPlaceholderLabel);

            return header;
        }

        private VisualElement BuildBody()
        {
            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;

            // Filled in later with the layer/animation tree.
            layersColumn = new VisualElement { name = "layers-column" };
            layersColumn.AddToClassList(LayersColumnUssClassName);
            layersColumn.style.width = SideColumnWidth;
            layersColumn.style.marginRight = 8f;
            body.Add(layersColumn);

            viewportColumn = new VisualElement { name = "viewport-column" };
            viewportColumn.AddToClassList(ViewportColumnUssClassName);
            viewportColumn.style.flexGrow = 1f;
            body.Add(viewportColumn);

            viewportStatusLabel = new Label();
            viewportStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            viewportColumn.Add(viewportStatusLabel);

            viewportImage = new Image();
            viewportImage.style.flexGrow = 1f;
            viewportImage.style.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
            viewportColumn.Add(viewportImage);

            // Filled in later with the profile/layer/animation inspector blocks.
            inspectorColumn = new VisualElement { name = "inspector-column" };
            inspectorColumn.AddToClassList(InspectorColumnUssClassName);
            inspectorColumn.style.width = SideColumnWidth;
            inspectorColumn.style.marginLeft = 8f;
            body.Add(inspectorColumn);

            return body;
        }

        // -----------------------------------------------------------------------------------------
        // Tick
        // -----------------------------------------------------------------------------------------

        // Renders whatever the controller currently holds — the rest pose with no clip sampled, or
        // its last sampled pose. The composited pose from the profile's layers lands later.
        private void Tick()
        {
            if (previewController == null)
            {
                return;
            }

            string status = previewController.StatusMessage;
            if (windowRig == null)
            {
                status = "No rig in the top bar — pick a profile to set it, or assign one directly.";
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
    }
}
