// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Right-hand inspector for the Cutouts tab's Assemble mode: piece list, selected-piece detail,
    /// output settings and the Build prefab action.
    /// </summary>
    public sealed class CutoutAssemblyInspector : VisualElement
    {
        public event Action<int> PieceSelected;
        public event Action AssemblyEdited;
        public event Action<string> StatusReported;

        private const string RootChoiceLabel = "(root)";
        private const float RotationQuarterTurnDegrees = 90f;

        private readonly VisualElement pieceListContainer;
        private readonly VisualElement detailBody;
        private readonly VisualElement detailCard;
        private readonly Label noAssemblyHint;
        private readonly VisualElement scrollContent;
        private readonly VisualElement outputKindControl;
        private readonly ObjectField materialField;
        private readonly TextField folderField;
        private readonly Label mergedRefusalLabel;

        private CutoutAssemblyAsset assembly;
        private int selectedPieceIndex = -1;

        public CutoutAssemblyInspector()
        {
            name = "cutouts-assembly-inspector";
            AddToClassList("toolkit-column");
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            VisualElement headerRow = ToolkitChrome.MakePaneHeader("Assembly", out _, out _);
            headerRow.style.flexShrink = 0f;
            Add(headerRow);

            ScrollView scrollView = new ScrollView { name = "cutouts-assembly-scroll" };
            scrollView.style.flexGrow = 1f;
            Add(scrollView);

            noAssemblyHint = ToolkitChrome.MakeHint("Pick or create an assembly to arrange its pieces.");
            scrollView.Add(noAssemblyHint);

            scrollContent = new VisualElement { name = "cutouts-assembly-content" };
            scrollView.Add(scrollContent);

            VisualElement piecesBody;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-assembly-card-pieces", "Pieces", out piecesBody, out _));
            pieceListContainer = new VisualElement { name = "cutouts-assembly-piece-list" };
            piecesBody.Add(pieceListContainer);

            detailCard = ToolkitChrome.MakeCard("cutouts-assembly-card-piece", "Selected piece", out detailBody, out _);
            scrollContent.Add(detailCard);

            VisualElement outputBody;
            scrollContent.Add(ToolkitChrome.MakeCard("cutouts-assembly-card-output", "Output", out outputBody, out _));

            outputKindControl = ToolkitChrome.MakeSegmentedControl("cutouts-assembly-output-kind",
                new[] { "Layered", "Merged" }, 0,
                (int selectedIndex) =>
                {
                    if (assembly == null)
                    {
                        return;
                    }

                    CutoutAssemblyOutputKind chosenKind = selectedIndex == 0 ? CutoutAssemblyOutputKind.LayeredSet : CutoutAssemblyOutputKind.Merged;
                    if (assembly.outputKind == chosenKind)
                    {
                        return;
                    }

                    RecordEdit("Change Assembly Output Kind");
                    assembly.outputKind = chosenKind;
                    CommitEdit();
                    RefreshMergedRefusal();
                });
            outputBody.Add(ToolkitChrome.MakePropertyRow("Kind", outputKindControl,
                "Layered keeps one mesh per piece (animatable); Merged bakes everything into one mesh."));

            materialField = new ObjectField { objectType = typeof(Material), allowSceneObjects = false };
            materialField.RegisterValueChangedCallback((ChangeEvent<UnityEngine.Object> changeEvent) =>
            {
                if (assembly == null)
                {
                    return;
                }

                RecordEdit("Change Assembly Material");
                assembly.material = changeEvent.newValue as Material;
                CommitEdit();
            });
            outputBody.Add(ToolkitChrome.MakePropertyRow("Material", materialField, "Material the assembled pieces are drawn with."));

            VisualElement folderRow = new VisualElement { name = "cutouts-assembly-folder-row" };
            folderRow.style.flexDirection = FlexDirection.Row;
            folderRow.style.flexGrow = 1f;
            folderField = new TextField();
            folderField.style.flexGrow = 1f;
            folderField.RegisterValueChangedCallback((ChangeEvent<string> changeEvent) =>
            {
                if (assembly == null)
                {
                    return;
                }

                RecordEdit("Change Assembly Folder");
                assembly.outputFolder = changeEvent.newValue;
                CommitEdit();
            });
            folderRow.Add(folderField);
            folderRow.Add(ToolkitChrome.MakeIconSquare(BrowseForOutputFolder, "d_Folder Icon", "Choose a folder inside Assets"));
            outputBody.Add(ToolkitChrome.MakePropertyRow("Folder", folderRow, "Project folder the output assets are written to."));

            mergedRefusalLabel = ToolkitChrome.MakeHint(string.Empty);
            mergedRefusalLabel.name = "cutouts-assembly-merged-refusal";
            mergedRefusalLabel.style.display = DisplayStyle.None;
            outputBody.Add(mergedRefusalLabel);

            Button buildButton = ToolkitChrome.MakePrimaryAction(BuildPrefab, "d_Prefab Icon", "Write the assembly's prefab, rig and meshes", "Build prefab");
            buildButton.name = "cutouts-assembly-build";
            buildButton.style.marginTop = 8f;
            outputBody.Add(buildButton);

            SetAssembly(null);
        }

        public void SetAssembly(CutoutAssemblyAsset newAssembly)
        {
            assembly = newAssembly;
            selectedPieceIndex = -1;
            Refresh();
        }

        public void SetSelectedPiece(int pieceIndex)
        {
            selectedPieceIndex = pieceIndex;
            RebuildPieceList();
            RebuildDetail();
        }

        public void Refresh()
        {
            bool hasAssembly = assembly != null;
            noAssemblyHint.style.display = hasAssembly ? DisplayStyle.None : DisplayStyle.Flex;
            scrollContent.style.display = hasAssembly ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasAssembly)
            {
                return;
            }

            if (selectedPieceIndex >= assembly.pieces.Count)
            {
                selectedPieceIndex = -1;
            }

            ToolkitChrome.SetSegmentedSelection(outputKindControl, assembly.outputKind == CutoutAssemblyOutputKind.LayeredSet ? 0 : 1);
            materialField.SetValueWithoutNotify(assembly.material);
            folderField.SetValueWithoutNotify(assembly.outputFolder ?? string.Empty);
            RebuildPieceList();
            RebuildDetail();
            RefreshMergedRefusal();
        }

        private void RecordEdit(string undoLabel)
        {
            Undo.RecordObject(assembly, undoLabel);
        }

        private void CommitEdit()
        {
            EditorUtility.SetDirty(assembly);
            AssemblyEdited?.Invoke();
        }

        private static string DisplayNameOf(CutoutAssemblyPiece piece, int pieceIndex)
        {
            if (!string.IsNullOrEmpty(piece.name))
            {
                return piece.name;
            }

            return piece.cutout != null ? piece.cutout.name : "Piece " + pieceIndex.ToString();
        }

        private bool WouldMakeCycle(int pieceIndex, int candidateParentIndex)
        {
            int hopsLeft = assembly.pieces.Count + 1;
            int walkIndex = candidateParentIndex;
            while (walkIndex >= 0 && walkIndex < assembly.pieces.Count && hopsLeft-- > 0)
            {
                if (walkIndex == pieceIndex)
                {
                    return true;
                }

                walkIndex = assembly.pieces[walkIndex].parentPieceIndex;
            }

            return false;
        }

        private void RebuildPieceList()
        {
            pieceListContainer.Clear();
            if (assembly == null)
            {
                return;
            }

            if (assembly.pieces.Count == 0)
            {
                pieceListContainer.Add(ToolkitChrome.MakeHint("No pieces yet. Add cutouts from the catalog."));
                return;
            }

            for (int pieceIndex = 0; pieceIndex < assembly.pieces.Count; pieceIndex++)
            {
                pieceListContainer.Add(MakePieceRow(pieceIndex));
            }
        }

        private VisualElement MakePieceRow(int pieceIndex)
        {
            CutoutAssemblyPiece piece = assembly.pieces[pieceIndex];
            VisualElement row;
            VisualElement slot = ToolkitChrome.MakeListRowSlot("cutouts-assembly-piece-" + pieceIndex.ToString(), out row);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.EnableInClassList("toolkit-list-row--selected", pieceIndex == selectedPieceIndex);

            Label nameLabel = new Label(DisplayNameOf(piece, pieceIndex));
            nameLabel.style.flexGrow = 1f;
            nameLabel.style.flexShrink = 1f;
            nameLabel.style.overflow = Overflow.Hidden;
            row.Add(nameLabel);

            Label orderLabel = new Label("z " + piece.drawOrder.ToString());
            orderLabel.style.marginLeft = 8f;
            orderLabel.style.marginRight = 8f;
            row.Add(orderLabel);

            List<string> parentChoices = new List<string> { RootChoiceLabel };
            List<int> parentChoicePieceIndices = new List<int> { -1 };
            for (int candidateIndex = 0; candidateIndex < assembly.pieces.Count; candidateIndex++)
            {
                if (candidateIndex == pieceIndex || WouldMakeCycle(pieceIndex, candidateIndex))
                {
                    continue;
                }

                parentChoices.Add(DisplayNameOf(assembly.pieces[candidateIndex], candidateIndex));
                parentChoicePieceIndices.Add(candidateIndex);
            }

            int currentChoice = Mathf.Max(0, parentChoicePieceIndices.IndexOf(piece.parentPieceIndex));
            DropdownField parentDropdown = new DropdownField(parentChoices, currentChoice);
            parentDropdown.tooltip = "Parent piece";
            parentDropdown.style.width = 96f;
            parentDropdown.style.flexShrink = 0f;
            parentDropdown.RegisterValueChangedCallback((ChangeEvent<string> changeEvent) =>
            {
                int chosenChoice = parentDropdown.index;
                if (chosenChoice < 0 || chosenChoice >= parentChoicePieceIndices.Count)
                {
                    return;
                }

                RecordEdit("Change Piece Parent");
                piece.parentPieceIndex = parentChoicePieceIndices[chosenChoice];
                CommitEdit();
                RebuildPieceList();
            });
            row.Add(parentDropdown);

            row.RegisterCallback<ClickEvent>((ClickEvent clickEvent) =>
            {
                VisualElement targetElement = clickEvent.target as VisualElement;
                if (targetElement != null && (targetElement is DropdownField || targetElement.GetFirstAncestorOfType<DropdownField>() != null))
                {
                    return;
                }

                SetSelectedPiece(pieceIndex);
                PieceSelected?.Invoke(pieceIndex);
            });
            return slot;
        }

        private void RebuildDetail()
        {
            detailBody.Clear();
            bool hasPiece = assembly != null && selectedPieceIndex >= 0 && selectedPieceIndex < assembly.pieces.Count;
            detailCard.style.display = hasPiece ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasPiece)
            {
                return;
            }

            CutoutAssemblyPiece piece = assembly.pieces[selectedPieceIndex];

            TextField nameField = new TextField { value = piece.name };
            nameField.RegisterValueChangedCallback((ChangeEvent<string> changeEvent) =>
            {
                RecordEdit("Rename Piece");
                piece.name = changeEvent.newValue;
                CommitEdit();
            });
            nameField.RegisterCallback<BlurEvent>((BlurEvent blurEvent) => RebuildPieceList());
            detailBody.Add(ToolkitChrome.MakePropertyRow("Name", nameField, "Piece name shown in the list and used for the child object."));

            VisualElement positionRow = new VisualElement();
            positionRow.style.flexDirection = FlexDirection.Row;
            positionRow.style.flexGrow = 1f;
            FloatField positionXField = new FloatField("X") { value = piece.positionWorld.x };
            FloatField positionYField = new FloatField("Y") { value = piece.positionWorld.y };
            positionXField.style.flexGrow = 1f;
            positionYField.style.flexGrow = 1f;
            positionXField.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) =>
            {
                RecordEdit("Move Piece");
                piece.positionWorld = new Vector2(changeEvent.newValue, piece.positionWorld.y);
                CommitEdit();
            });
            positionYField.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) =>
            {
                RecordEdit("Move Piece");
                piece.positionWorld = new Vector2(piece.positionWorld.x, changeEvent.newValue);
                CommitEdit();
            });
            positionRow.Add(positionXField);
            positionRow.Add(positionYField);
            detailBody.Add(ToolkitChrome.MakePropertyRow("Position", positionRow, "Where the piece's origin sits in the assembly, in world units."));

            VisualElement rotationRow = new VisualElement();
            rotationRow.style.flexDirection = FlexDirection.Row;
            rotationRow.style.alignItems = Align.Center;
            rotationRow.style.flexGrow = 1f;
            FloatField rotationField = new FloatField { value = piece.extraRotationDegrees };
            rotationField.style.flexGrow = 1f;
            rotationField.RegisterValueChangedCallback((ChangeEvent<float> changeEvent) =>
            {
                RecordEdit("Rotate Piece");
                piece.extraRotationDegrees = changeEvent.newValue;
                CommitEdit();
            });
            rotationRow.Add(rotationField);
            rotationRow.Add(MakeQuarterTurnButton(piece, rotationField, RotationQuarterTurnDegrees, "Rotate 90 degrees counter-clockwise", "⟲ 90"));
            rotationRow.Add(MakeQuarterTurnButton(piece, rotationField, -RotationQuarterTurnDegrees, "Rotate 90 degrees clockwise", "⟳ 90"));
            detailBody.Add(ToolkitChrome.MakePropertyRow("Rotation", rotationRow, "Rotation added on top of the cutout's own, in degrees."));

            IntegerField drawOrderField = new IntegerField { value = piece.drawOrder };
            drawOrderField.RegisterValueChangedCallback((ChangeEvent<int> changeEvent) =>
            {
                RecordEdit("Change Piece Draw Order");
                piece.drawOrder = changeEvent.newValue;
                CommitEdit();
                RebuildPieceList();
            });
            detailBody.Add(ToolkitChrome.MakePropertyRow("Draw order", drawOrderField, "Higher draws in front."));
        }

        private Button MakeQuarterTurnButton(CutoutAssemblyPiece piece, FloatField rotationField, float deltaDegrees, string tooltip, string text)
        {
            Button quarterTurnButton = ToolkitChrome.MakeSecondaryAction(() =>
            {
                RecordEdit("Rotate Piece");
                piece.extraRotationDegrees += deltaDegrees;
                rotationField.SetValueWithoutNotify(piece.extraRotationDegrees);
                CommitEdit();
            }, "d_RotateTool", tooltip, text);
            quarterTurnButton.style.marginLeft = 4f;
            return quarterTurnButton;
        }

        private void BrowseForOutputFolder()
        {
            if (assembly == null)
            {
                return;
            }

            string chosenAbsolutePath = EditorUtility.OpenFolderPanel("Assembly output folder", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(chosenAbsolutePath))
            {
                return;
            }

            string normalizedDataPath = Application.dataPath.Replace('\\', '/');
            string normalizedChosenPath = chosenAbsolutePath.Replace('\\', '/');
            if (normalizedChosenPath != normalizedDataPath && !normalizedChosenPath.StartsWith(normalizedDataPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                StatusReported?.Invoke("Pick a folder inside this project's Assets folder.");
                return;
            }

            string projectRelativePath = "Assets" + normalizedChosenPath.Substring(normalizedDataPath.Length);
            RecordEdit("Change Assembly Folder");
            assembly.outputFolder = projectRelativePath;
            folderField.SetValueWithoutNotify(projectRelativePath);
            CommitEdit();
        }

        private string BuildMergedRefusalMessage()
        {
            if (assembly == null || assembly.outputKind != CutoutAssemblyOutputKind.Merged)
            {
                return null;
            }

            for (int pieceIndex = 0; pieceIndex < assembly.pieces.Count; pieceIndex++)
            {
                CutoutAssemblyPiece piece = assembly.pieces[pieceIndex];
                if (piece.cutout != null && piece.cutout.IsSheetFlipbook)
                {
                    return "Merged assemblies take still pieces; put '" + DisplayNameOf(piece, pieceIndex) + "' in a Layered set.";
                }
            }

            return null;
        }

        private void RefreshMergedRefusal()
        {
            string refusal = BuildMergedRefusalMessage();
            mergedRefusalLabel.text = refusal ?? string.Empty;
            mergedRefusalLabel.style.display = refusal == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void BuildPrefab()
        {
            if (assembly == null)
            {
                return;
            }

            string refusal = BuildMergedRefusalMessage();
            if (refusal != null)
            {
                RefreshMergedRefusal();
                StatusReported?.Invoke(refusal);
                return;
            }

            string report;
            if (assembly.outputKind == CutoutAssemblyOutputKind.Merged)
            {
                CutoutAssemblyMergedWriter.TryWrite(assembly, out report);
            }
            else
            {
                CutoutAssemblyLayeredWriter.TryWrite(assembly, out report);
            }

            StatusReported?.Invoke(report);
            AssemblyEdited?.Invoke();
        }
    }
}
