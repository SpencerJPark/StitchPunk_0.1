// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Actor Editor's first column: the shared clip set and rig fields over a searchable profile catalog with New, Refresh, and row Rename/Delete.</summary>
    public sealed class ActorEditorProfilesColumn : VisualElement, IDisposable
    {
        public event Action<ActorProfileAsset> ProfileSelected;

        public ActorProfileAsset SelectedProfile { get; private set; }

        private readonly ObjectField clipSetField;
        private readonly ObjectField rigField;
        private readonly ActorProfileCatalogColumn catalog;
        private readonly ActorProfileSaveLocation saveLocation = new ActorProfileSaveLocation();

        private ActiveAssetSelection selection;

        public ActorEditorProfilesColumn()
        {
            name = "profiles-column";
            style.minWidth = 200f;
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;

            clipSetField = new ObjectField
            {
                name = "actor-editor-clip-set-field",
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false,
                tooltip = "The clip set every tab is working on. Not what this profile plays — a profile lists its own clip sets."
            };
            clipSetField.AddToClassList("clip-editor__pane-field");
            clipSetField.RegisterValueChangedCallback(OnClipSetFieldChanged);
            Add(clipSetField);

            rigField = new ObjectField
            {
                name = "actor-editor-rig-field",
                objectType = typeof(RigAsset),
                allowSceneObjects = false,
                tooltip = "The rig every tab is working on. Picking a profile sets it to the profile's rig."
            };
            rigField.AddToClassList("clip-editor__pane-field");
            rigField.RegisterValueChangedCallback(OnRigFieldChanged);
            Add(rigField);

            catalog = new ActorProfileCatalogColumn();
            catalog.NewRequested += CreateAndSelectNewProfile;
            catalog.RefreshRequested += RescanProject;
            catalog.ProfileSelected += OnCatalogProfileSelected;
            catalog.ProfileRenameRequested += RenameProfileAndRefresh;
            catalog.ProfileDeleteRequested += RequestDeleteProfile;
            Add(catalog);
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSelectionClipSetChanged;
                selection.RigChanged -= OnSelectionRigChanged;
            }

            selection = sharedSelection;

            if (selection != null)
            {
                selection.ClipSetChanged += OnSelectionClipSetChanged;
                selection.RigChanged += OnSelectionRigChanged;
                clipSetField.SetValueWithoutNotify(selection.ClipSet);
                rigField.SetValueWithoutNotify(selection.Rig);
            }
        }

        public void SetSelectedProfile(ActorProfileAsset profile)
        {
            SelectedProfile = profile;
            catalog.SetSelectedProfile(profile);
        }

        public void RescanProject()
        {
            List<ActorProfileAsset> profiles = new List<ActorProfileAsset>();
            string[] profileAssetGuids = AssetDatabase.FindAssets("t:" + nameof(ActorProfileAsset));
            for (int guidIndex = 0; guidIndex < profileAssetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(profileAssetGuids[guidIndex]);
                ActorProfileAsset profile = AssetDatabase.LoadAssetAtPath<ActorProfileAsset>(assetPath);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }

            profiles.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name));
            LoadCatalog(profiles);
        }

        public void LoadCatalog(IReadOnlyList<ActorProfileAsset> profiles)
        {
            catalog.SetProfiles(profiles);
            catalog.SetSelectedProfile(SelectedProfile);
        }

        public void CreateAndSelectNewProfile()
        {
            string folder = saveLocation.Recall();
            string assetPath = ActorProfileSaveLocation.ResolveTargetAssetPath(
                folder, ActorProfileSaveLocation.DefaultAssetName);
            ActorProfileAsset createdProfile = ActorProfileAssetUtility.CreateProfile(assetPath);
            if (createdProfile == null)
            {
                return;
            }

            RescanProject();
            SelectedProfile = createdProfile;
            catalog.SetSelectedProfile(createdProfile);
            ProfileSelected?.Invoke(createdProfile);
        }

        public void Dispose()
        {
            if (selection != null)
            {
                selection.ClipSetChanged -= OnSelectionClipSetChanged;
                selection.RigChanged -= OnSelectionRigChanged;
                selection = null;
            }
        }

        private void OnClipSetFieldChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            selection?.SetClipSet(changeEvent.newValue as ClipSetAsset);
        }

        private void OnRigFieldChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            selection?.SetRig(changeEvent.newValue as RigAsset);
        }

        private void OnSelectionClipSetChanged(ClipSetAsset clipSet)
        {
            clipSetField.SetValueWithoutNotify(clipSet);
        }

        private void OnSelectionRigChanged(RigAsset rig)
        {
            rigField.SetValueWithoutNotify(rig);
        }

        private void OnCatalogProfileSelected(ActorProfileAsset picked)
        {
            SelectedProfile = picked;
            ProfileSelected?.Invoke(picked);
        }

        private void RenameProfileAndRefresh(ActorProfileAsset profile, string requestedName)
        {
            if (profile == null || requestedName == profile.name)
            {
                return;
            }

            ActorProfileAssetUtility.RenameProfile(profile, requestedName);
            RescanProject();
            SelectedProfile = profile;
            catalog.SetSelectedProfile(profile);
            ProfileSelected?.Invoke(profile);
        }

        private void RequestDeleteProfile(ActorProfileAsset profile)
        {
            if (profile == null)
            {
                return;
            }

            string referenceSummary = AssetReferenceIndex.SummarizeForDialog(AssetReferenceIndex.ReferencesToProfile(profile));
            string dialogBody = "Move \"" + profile.name + "\" to the trash? Actors and cutscenes that reference it will lose their profile.";
            if (referenceSummary.Length > 0)
            {
                dialogBody = referenceSummary + "\n\n" + dialogBody;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Actor Profile",
                dialogBody,
                "Move to Trash",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            // The catalog and this column both hold a reference to this profile -- clearing the
            // selection before trashing keeps the editor from pointing at an asset that no longer exists.
            SelectedProfile = null;
            catalog.ClearSelection();
            ProfileSelected?.Invoke(null);

            ActorProfileAssetUtility.TrashProfile(profile);
            RescanProject();
        }
    }
}
