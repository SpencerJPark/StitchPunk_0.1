#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

// ===========================================================================
//  Source Image node
//  One dragged-in texture. Shows a thumbnail and exposes its four channels as
//  output ports, each of which may feed any number of output channels.
// ===========================================================================

/// <summary>Port and header helpers shared by both node views in this window.</summary>
public static class TexturePackerNodeUI
{
    public static Port MakePort(Node ownerNode,
                                UnityEditor.Experimental.GraphView.Direction direction,
                                Port.Capacity capacity,
                                string portName,
                                Color portColor)
    {
        Port port = ownerNode.InstantiatePort(Orientation.Horizontal, direction, capacity, typeof(float));
        port.portName = portName;
        port.portColor = portColor;
        return port;
    }

    public static void SetHeaderColor(Node node, Color headerColor)
    {
        VisualElement titleBar = node.Q("title");
        if (titleBar != null)
        {
            titleBar.style.backgroundColor = headerColor;
        }
    }
}

public class SourceImageNodeView : Node
{
    private static readonly Color HeaderColor = new Color(0.16f, 0.32f, 0.42f);
    private static readonly Color MissingHeaderColor = new Color(0.52f, 0.14f, 0.14f);
    private const float ThumbnailSize = 96f;

    /// <summary>GUID of the source asset. Survives moves and renames; the recipe stores this, not a path.</summary>
    public string TextureGuid { get; }

    /// <summary>The imported texture, or null when the GUID no longer resolves to an asset.</summary>
    public Texture2D SourceTexture { get; }

    public bool IsMissing => SourceTexture == null;

    private readonly Port[] channelPorts = new Port[PackChannelIndex.Count];

    public SourceImageNodeView(string textureGuid, Texture2D sourceTexture)
    {
        TextureGuid = textureGuid;
        SourceTexture = sourceTexture;

        if (IsMissing)
        {
            title = "Missing source";
            TexturePackerNodeUI.SetHeaderColor(this, MissingHeaderColor);
            BuildMissingBody(textureGuid);
        }
        else
        {
            title = sourceTexture.name;
            TexturePackerNodeUI.SetHeaderColor(this, HeaderColor);
            BuildThumbnailBody(sourceTexture);
        }

        // Four output ports — one per source channel. Multi capacity, because the same
        // greyscale channel is often wired into more than one output slot.
        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            Port channelPort = TexturePackerNodeUI.MakePort(
                this,
                UnityEditor.Experimental.GraphView.Direction.Output,
                Port.Capacity.Multi,
                PackChannelIndex.Names[channelIndex],
                PackChannelIndex.PortColors[channelIndex]);

            channelPorts[channelIndex] = channelPort;
            outputContainer.Add(channelPort);
        }

        RefreshExpandedState();
        RefreshPorts();
    }

    private void BuildThumbnailBody(Texture2D sourceTexture)
    {
        Image thumbnail = new Image();
        thumbnail.image = sourceTexture;
        thumbnail.scaleMode = ScaleMode.ScaleToFit;
        thumbnail.style.width = ThumbnailSize;
        thumbnail.style.height = ThumbnailSize;
        thumbnail.style.marginTop = 4f;
        thumbnail.style.marginBottom = 2f;
        thumbnail.style.alignSelf = Align.Center;
        extensionContainer.Add(thumbnail);

        Label sizeLabel = new Label(sourceTexture.width + " x " + sourceTexture.height);
        sizeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        sizeLabel.style.fontSize = 10;
        sizeLabel.style.opacity = 0.7f;
        sizeLabel.style.marginBottom = 4f;
        extensionContainer.Add(sizeLabel);
    }

    private void BuildMissingBody(string textureGuid)
    {
        Label missingLabel = new Label("Asset not found\n" + textureGuid);
        missingLabel.style.whiteSpace = WhiteSpace.Normal;
        missingLabel.style.fontSize = 10;
        missingLabel.style.width = ThumbnailSize + 40f;
        missingLabel.style.marginTop = 4f;
        missingLabel.style.marginBottom = 4f;
        missingLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        extensionContainer.Add(missingLabel);
    }

    public Port GetChannelPort(int channelIndex)
    {
        return channelPorts[channelIndex];
    }

    /// <summary>Which of this node's channels the given port represents, or -1 if the port is not ours.</summary>
    public int FindChannelIndex(Port port)
    {
        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            if (channelPorts[channelIndex] == port)
            {
                return channelIndex;
            }
        }
        return -1;
    }

    /// <summary>Builds a node for an asset GUID, resolving it through the AssetDatabase.</summary>
    public static SourceImageNodeView CreateFromGuid(string textureGuid)
    {
        string assetPath = AssetDatabase.GUIDToAssetPath(textureGuid);
        Texture2D sourceTexture = string.IsNullOrEmpty(assetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        return new SourceImageNodeView(textureGuid, sourceTexture);
    }
}
#endif
