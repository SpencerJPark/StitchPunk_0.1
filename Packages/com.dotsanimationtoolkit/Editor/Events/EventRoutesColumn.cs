// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Events tab's right column: the selected key's routes in the project routing asset, and the consumer stub button.</summary>
    public sealed class EventRoutesColumn : VisualElement
    {
        private const string StubNamePrefsKey = "DotsAnimationToolkit.Events.StubName";

        private readonly VisualElement rowsContainer;
        private readonly Button addRouteButton;
        private readonly TextField stubNameField;

        public AnimEventKeyEntry BoundEntry { get; private set; }

        public EventRoutesColumn()
        {
            name = "events-routes-column";
            style.paddingTop = 8;
            style.paddingLeft = 10;
            style.paddingRight = 10;
            style.flexGrow = 1;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");
            Label title = new Label("Routes");
            title.AddToClassList("toolkit-pane-title");
            header.Add(title);
            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");
            this.addRouteButton = ToolkitIcons.MakeIconTextButton(this.AddRoute, ToolkitIcons.Plus, "Add a route for the selected event.", "Route");
            this.addRouteButton.name = "events-routes-add-button";
            this.addRouteButton.SetEnabled(false);
            actions.Add(this.addRouteButton);
            header.Add(actions);
            Add(header);

            this.rowsContainer = new VisualElement();
            Add(this.rowsContainer);

            VisualElement stubSection = new VisualElement();
            this.stubNameField = new TextField("System name")
            {
                name = "events-routes-stub-name",
                value = EditorPrefs.GetString(StubNamePrefsKey, "Project"),
            };
            this.stubNameField.RegisterValueChangedCallback(changeEvent => EditorPrefs.SetString(StubNamePrefsKey, changeEvent.newValue));
            stubSection.Add(this.stubNameField);

            Button generateStubButton = new Button(this.GenerateConsumerStub)
            {
                name = "events-routes-stub-button",
                text = "Generate consumer stub…",
            };
            stubSection.Add(generateStubButton);

            Label stubHint = new Label("Writes a system into your project. The package never handles a route.");
            stubHint.AddToClassList("clip-editor__hint");
            stubSection.Add(stubHint);

            Add(stubSection);

            RefreshRows();
        }

        public void Bind(AnimEventKeyEntry entry)
        {
            this.BoundEntry = entry;
            RefreshRows();
        }

        public void RefreshRows()
        {
            this.rowsContainer.Clear();
            this.addRouteButton.SetEnabled(this.BoundEntry != null);

            if (this.BoundEntry == null)
            {
                this.rowsContainer.Add(MakeHint("Select an event on the left."));
                return;
            }

            AnimEventRoutingAsset routingAsset = AnimEventRoutingAssetUtility.FindDefault();
            if (routingAsset == null)
            {
                this.rowsContainer.Add(MakeHint("No routing asset yet. Adding a route creates one at " + AnimEventRoutingAssetUtility.DefaultAssetPath));
                return;
            }

            List<AnimEventRoute> routes = AnimEventRoutingAssetUtility.RoutesForKey(routingAsset, this.BoundEntry.eventKey);
            if (routes == null || routes.Count == 0)
            {
                this.rowsContainer.Add(MakeHint("No routes for this event."));
                return;
            }

            foreach (AnimEventRoute route in routes)
            {
                this.rowsContainer.Add(this.MakeRouteRow(routingAsset, route));
            }
        }

        private VisualElement MakeRouteRow(AnimEventRoutingAsset routingAsset, AnimEventRoute route)
        {
            VisualElement row = new VisualElement();
            row.style.borderTopWidth = 1;
            row.style.borderBottomWidth = 1;
            row.style.borderLeftWidth = 1;
            row.style.borderRightWidth = 1;
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;
            row.style.marginTop = 4;
            row.style.marginBottom = 4;
            row.style.marginLeft = 4;
            row.style.marginRight = 4;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;

            EnumField kindField = new EnumField("Kind", route.kind);
            kindField.RegisterValueChangedCallback(changeEvent =>
            {
                Undo.RecordObject(routingAsset, "Edit Event Route");
                route.kind = (AnimEventRouteKind)changeEvent.newValue;
                AnimEventRoutingAssetUtility.Persist(routingAsset);
            });
            row.Add(kindField);

            TextField routeIdField = new TextField("Route id")
            {
                isDelayed = true,
                value = "0x" + route.routeId.ToString("X8"),
            };
            routeIdField.RegisterValueChangedCallback(changeEvent =>
            {
                string rawText = changeEvent.newValue;
                if (rawText.StartsWith("0x") || rawText.StartsWith("0X"))
                {
                    rawText = rawText.Substring(2);
                }

                if (uint.TryParse(rawText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsedRouteId))
                {
                    Undo.RecordObject(routingAsset, "Edit Event Route");
                    route.routeId = parsedRouteId;
                    AnimEventRoutingAssetUtility.Persist(routingAsset);
                }
                else
                {
                    routeIdField.SetValueWithoutNotify(changeEvent.previousValue);
                }
            });
            row.Add(routeIdField);

            TextField noteField = new TextField("Note")
            {
                isDelayed = true,
                value = route.note,
            };
            noteField.RegisterValueChangedCallback(changeEvent =>
            {
                Undo.RecordObject(routingAsset, "Edit Event Route");
                route.note = changeEvent.newValue;
                AnimEventRoutingAssetUtility.Persist(routingAsset);
            });
            row.Add(noteField);

            ObjectField displayAssetField = new ObjectField("Display asset")
            {
                objectType = typeof(Object),
                allowSceneObjects = false,
                value = route.displayAsset,
            };
            displayAssetField.RegisterValueChangedCallback(changeEvent =>
            {
                Undo.RecordObject(routingAsset, "Edit Event Route");
                route.displayAsset = changeEvent.newValue;
                AnimEventRoutingAssetUtility.Persist(routingAsset);
            });
            row.Add(displayAssetField);

            Button removeButton = ToolkitIcons.MakeIconButton(
                () =>
                {
                    AnimEventRoutingAssetUtility.RemoveRoute(routingAsset, route);
                    RefreshRows();
                },
                ToolkitIcons.Trash,
                "Remove this route.",
                "x");
            row.Add(removeButton);

            return row;
        }

        private void AddRoute()
        {
            if (this.BoundEntry == null)
            {
                return;
            }

            AnimEventRoutingAsset routingAsset = AnimEventRoutingAssetUtility.GetOrCreateDefault();
            if (routingAsset == null)
            {
                return;
            }

            AnimEventRoutingAssetUtility.AddRoute(routingAsset, this.BoundEntry.eventKey);
            RefreshRows();
        }

        private void GenerateConsumerStub()
        {
            string startFolder = EditorPrefs.GetString(AnimEventConsumerStubBuilder.FolderPrefsKey, "Assets");
            string chosenFolder = EditorUtility.OpenFolderPanel("Folder for the consumer stub", startFolder, "");
            if (string.IsNullOrEmpty(chosenFolder))
            {
                return;
            }

            chosenFolder = chosenFolder.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (!chosenFolder.StartsWith(dataPath))
            {
                EditorUtility.DisplayDialog("Generate Consumer Stub", "Pick a folder inside this project's Assets folder.", "OK");
                return;
            }

            string systemName = this.stubNameField.value;
            string targetPath = chosenFolder + "/" + AnimEventConsumerStubBuilder.SystemTypeName(systemName) + ".cs";
            if (File.Exists(targetPath))
            {
                bool confirmOverwrite = EditorUtility.DisplayDialog("Generate Consumer Stub", "Overwrite " + targetPath + "?", "Overwrite", "Cancel");
                if (!confirmOverwrite)
                {
                    return;
                }
            }

            EditorPrefs.SetString(AnimEventConsumerStubBuilder.FolderPrefsKey, chosenFolder);

            string writtenPath = AnimEventConsumerStubBuilder.WriteToFolder(
                chosenFolder,
                systemName,
                AnimEventConsumerStubBuilder.CollectKindsUsed(AnimEventRoutingAssetUtility.FindDefault()));

            Debug.Log("[DOTS Animation Toolkit] Wrote consumer stub " + ConstantsGenerator.ToStorablePath(writtenPath));
        }

        private static Label MakeHint(string text)
        {
            Label hint = new Label(text);
            hint.AddToClassList("clip-editor__hint");
            return hint;
        }
    }
}
