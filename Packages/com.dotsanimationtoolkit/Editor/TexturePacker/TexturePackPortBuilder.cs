// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Port and header helpers shared by every node view in the texture packer graph.</summary>
    public static class TexturePackPortBuilder
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
}
