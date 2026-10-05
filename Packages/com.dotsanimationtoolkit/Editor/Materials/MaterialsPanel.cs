// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab: author and check a material from a cutout or a mesh, with a rig optional for the shader-contract check.</summary>
    public sealed class MaterialsPanel : VisualElement, IDisposable
    {
        private const string MaterialsModeName = "materials";
        private const string CutoutsModeName = "cutouts";
        private const string MeshesModeName = "meshes";

        private readonly List<RigMaterialUsage> usages = new List<RigMaterialUsage>();
        private readonly ObjectField rigField;
        private readonly ObjectField clipSetField;
        private readonly Label resultLabel;
        private readonly MaterialCatalogColumn materialCatalog;
        private readonly CutoutCatalogColumn cutoutCatalog;
        private readonly MeshCatalogColumn meshCatalog;
        private readonly CatalogSidebarElement sidebar;
        private readonly MaterialInspectorColumn inspector;
        private ActiveAssetSelection selection;
        private MaterialInspectorSubject currentSubject = new MaterialInspectorSubject();
        private bool isRefreshing;

        public RigAsset BoundRig { get; private set; }
        public ClipSetAsset BoundClipSet { get; private set; }
        public Material SelectedMaterial { get; private set; }

        public IReadOnlyList<RigMaterialUsage> Usages
        {
            get { return usages; }
        }

        public MaterialsPanel()
        {
            style.flexGrow = 1f;

            VisualElement header = ToolkitChrome.MakeAssetBar("materials-asset-bar");
            header.Add(ToolkitChrome.MakeAssetBarLabel("Check against"));

            header.Add(ToolkitChrome.MakeAssetBarLabel("Rig"));
            rigField = new ObjectField
            {
                objectType = typeof(RigAsset),
                allowSceneObjects = false,
                name = "materials-rig-field"
            };
            rigField.AddToClassList("toolkit-asset-bar__field");
            rigField.RegisterValueChangedCallback(changeEvent =>
            {
                RigAsset newRig = changeEvent.newValue as RigAsset;
                if (selection != null) { selection.SetRig(newRig); } else { SetRig(newRig); }
            });
            header.Add(rigField);

            header.Add(ToolkitChrome.MakeAssetBarLabel("Clip Set"));
            clipSetField = new ObjectField
            {
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false,
                name = "materials-clip-set-field"
            };
            clipSetField.AddToClassList("toolkit-asset-bar__field");
            clipSetField.RegisterValueChangedCallback(changeEvent =>
            {
                ClipSetAsset newClipSet = changeEvent.newValue as ClipSetAsset;
                if (selection != null) { selection.SetClipSet(newClipSet); } else { SetClipSet(newClipSet); }
            });
            header.Add(clipSetField);

            VisualElement statusRow = ToolkitChrome.MakeStatusRow(out resultLabel, out _, true);
            resultLabel.name = "materials-result";

            materialCatalog = new MaterialCatalogColumn();
            materialCatalog.MaterialSelected += SelectMaterial;
            materialCatalog.RefreshRequested += Refresh;
            materialCatalog.NewRequested += OnNewMaterialRequested;

            cutoutCatalog = new CutoutCatalogColumn();
            cutoutCatalog.AssetSelected += OnCutoutPicked;
            cutoutCatalog.RefreshRequested += Refresh;
            cutoutCatalog.NewRequested += OnNewCutoutRequested;

            meshCatalog = new MeshCatalogColumn();
            meshCatalog.MeshSelected += OnMeshPicked;
            meshCatalog.RefreshRequested += Refresh;
            meshCatalog.NewRequested += OnNewMeshRequested;

            sidebar = new CatalogSidebarElement { name = "materials-sidebar" };
            sidebar.AddMode(MaterialsModeName, "Materials", materialCatalog, materialCatalog.HeaderActions);
            sidebar.AddMode(CutoutsModeName, "Cutouts", cutoutCatalog, cutoutCatalog.HeaderActions);
            sidebar.AddMode(MeshesModeName, "Meshes", meshCatalog, meshCatalog.HeaderActions);
            sidebar.SetMode(MaterialsModeName);
            sidebar.ModeChanged += OnSidebarModeChanged;

            inspector = new MaterialInspectorColumn();
            inspector.MaterialCreated += OnInspectorMaterialCreated;
            inspector.MaterialChanged += OnInspectorMaterialChanged;
            inspector.StatusReported += (text, tone) => ToolkitChrome.SetStatus(resultLabel, text, tone);

            CoverPaneSplitView split = new CoverPaneSplitView("Materials.Catalog", 0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            split.Add(sidebar);
            split.Add(inspector);

            Add(header);
            Add(split);
            Add(statusRow);
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }

            selection = sharedSelection;
            selection.RigChanged += OnSharedRigChanged;
            selection.ClipSetChanged += OnSharedClipSetChanged;
            SetClipSet(selection.ClipSet);
            SetRig(selection.Rig);
        }

        public void SetRig(RigAsset rig)
        {
            BoundRig = rig;
            rigField.SetValueWithoutNotify(rig);
            Refresh();
        }

        public void SetClipSet(ClipSetAsset clipSet)
        {
            BoundClipSet = clipSet;
            clipSetField.SetValueWithoutNotify(clipSet);
            RebindCurrentSubject();
        }

        public void Refresh()
        {
            // Each column's Rescan raises RefreshRequested, which is wired back here: without the guard
            // the first rescan recursed until the stack overflowed and no list was ever filled.
            if (isRefreshing)
            {
                return;
            }

            isRefreshing = true;
            try
            {
                usages.Clear();
                if (BoundRig != null)
                {
                    usages.AddRange(RigMaterialResolver.Resolve(BoundRig));
                }

                materialCatalog.SetUsages(usages);
                cutoutCatalog.RescanProject();
                meshCatalog.RescanProject();
            }
            finally
            {
                isRefreshing = false;
            }

            if (sidebar.Mode == MaterialsModeName && SelectedMaterial == null && usages.Count > 0)
            {
                SelectedMaterial = usages[0].Material;
                materialCatalog.SetSelectedMaterial(SelectedMaterial);
                currentSubject = BuildSubject(SelectedMaterial, null, null);
            }

            RebindCurrentSubject();
        }

        public void SelectMaterial(Material material)
        {
            SelectedMaterial = material;
            materialCatalog.SetSelectedMaterial(material);
            currentSubject = BuildSubject(material, null, null);
            RebindCurrentSubject();
        }

        public void Dispose()
        {
            if (selection != null)
            {
                selection.RigChanged -= OnSharedRigChanged;
                selection.ClipSetChanged -= OnSharedClipSetChanged;
            }
        }

        private void OnSharedRigChanged(RigAsset rig)
        {
            SetRig(rig);
        }

        private void OnSharedClipSetChanged(ClipSetAsset clipSet)
        {
            SetClipSet(clipSet);
        }

        private void OnCutoutPicked(CutoutAsset cutout)
        {
            currentSubject = BuildSubjectForCutout(cutout);
            RebindCurrentSubject();
        }

        private void OnMeshPicked(Mesh mesh)
        {
            currentSubject = BuildSubjectForMesh(mesh);
            RebindCurrentSubject();
        }

        private void OnSidebarModeChanged(string modeName)
        {
            if (modeName == CutoutsModeName)
            {
                currentSubject = BuildSubjectForCutout(cutoutCatalog.SelectedAsset);
            }
            else if (modeName == MeshesModeName)
            {
                currentSubject = BuildSubjectForMesh(meshCatalog.SelectedAsset);
            }
            else
            {
                currentSubject = BuildSubject(SelectedMaterial, null, null);
            }

            RebindCurrentSubject();
        }

        private void OnInspectorMaterialCreated(Material createdMaterial)
        {
            MaterialInspectorSubject previousSubject = currentSubject;
            Refresh();

            if (previousSubject.Cutout != null || previousSubject.Mesh != null)
            {
                currentSubject = BuildSubject(createdMaterial, previousSubject.Cutout, previousSubject.Mesh);
                RebindCurrentSubject();
            }
            else
            {
                SelectMaterial(createdMaterial);
            }
        }

        private void OnInspectorMaterialChanged(Material changedMaterial)
        {
            materialCatalog.RefreshRows();
            RebindCurrentSubject();
        }

        private void OnNewMaterialRequested()
        {
            string assetPath = EditorUtility.SaveFilePanelInProject("New material", "M_New", "mat", "Where to save the material");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (MaterialAuthoringUtility.TryCreateMaterial(assetPath, MaterialFeature.None, null, out Material createdMaterial, out string failureMessage))
            {
                ToolkitChrome.SetStatus(resultLabel, "Created " + Path.GetFileName(assetPath) + ".", ToolkitStatusTone.Neutral);
                Refresh();
                SelectMaterial(createdMaterial);
            }
            else
            {
                ToolkitChrome.SetStatus(resultLabel, failureMessage, ToolkitStatusTone.Error);
            }
        }

        private void OnNewCutoutRequested()
        {
            ToolkitChrome.SetStatus(resultLabel, "Make cutouts in the Cutouts tab.", ToolkitStatusTone.Neutral);
        }

        private void OnNewMeshRequested()
        {
            sidebar.SetMode(CutoutsModeName);
        }

        private void RebindCurrentSubject()
        {
            currentSubject.RigUsage = FindUsageForMaterial(currentSubject.Material);
            inspector.Bind(currentSubject, BoundRig, BoundClipSet);
        }

        private MaterialInspectorSubject BuildSubject(Material material, CutoutAsset cutout, Mesh mesh)
        {
            return new MaterialInspectorSubject
            {
                Material = material,
                Cutout = cutout,
                Mesh = mesh,
                RigUsage = FindUsageForMaterial(material)
            };
        }

        private MaterialInspectorSubject BuildSubjectForCutout(CutoutAsset cutout)
        {
            if (cutout == null)
            {
                return new MaterialInspectorSubject();
            }

            Material material = cutout.material;
            if (material == null && cutout.outputMesh != null)
            {
                material = MaterialFeatureResolver.FindMaterialForMesh(cutout.outputMesh);
            }

            return BuildSubject(material, cutout, cutout.outputMesh);
        }

        private MaterialInspectorSubject BuildSubjectForMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return new MaterialInspectorSubject();
            }

            return BuildSubject(MaterialFeatureResolver.FindMaterialForMesh(mesh), null, mesh);
        }

        private RigMaterialUsage FindUsageForMaterial(Material material)
        {
            if (material == null)
            {
                return null;
            }

            foreach (RigMaterialUsage usage in usages)
            {
                if (usage.Material == material)
                {
                    return usage;
                }
            }

            return null;
        }
    }
}
