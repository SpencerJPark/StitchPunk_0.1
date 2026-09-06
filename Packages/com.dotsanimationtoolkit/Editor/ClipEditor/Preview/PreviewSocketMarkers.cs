// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Draws every socket where it will actually be, and rides whatever the user pinned to it. Both
    /// rig-target and bone sockets are previewed, composed exactly as <c>SocketResolveSystem</c>
    /// does: the followed transform's local pose, then the socket's own offset rotated into it.
    /// </summary>
    public sealed class PreviewSocketMarkers
    {
        /// <summary>Edge length of a socket marker cube, in world units.</summary>
        private const float MarkerSize = 0.06f;

        private GameObject rootObject;
        private readonly List<SocketDefinition> sockets = new List<SocketDefinition>();
        private readonly List<Transform> markers = new List<Transform>();
        private readonly List<GameObject> attachments = new List<GameObject>();

        /// <summary>The markers' shared root, or null before <see cref="Rebuild"/> has run.</summary>
        public GameObject RootObject
        {
            get { return rootObject; }
        }

        /// <summary>How many sockets currently have a marker.</summary>
        public int MarkerCount
        {
            get { return markers.Count; }
        }

        /// <summary>The marker transform for a socket id, or null when it has none.</summary>
        public Transform GetMarker(uint socketId)
        {
            int index = IndexOf(socketId);
            return index >= 0 ? markers[index] : null;
        }

        // The socket a picked transform stands for, or false when it is not a marker. Walks up from
        // the picked transform, since a click usually lands on a child of an attachment (the blade
        // of the sword), not the socket cube.
        public bool TryGetSocketId(Transform picked, out uint socketId)
        {
            socketId = 0u;
            Transform walker = picked;
            while (walker != null)
            {
                int index = markers.IndexOf(walker);
                if (index >= 0)
                {
                    socketId = sockets[index].Id.Value;
                    return true;
                }
                walker = walker.parent;
            }
            return false;
        }

        // Rebuilds a marker per socket the rig declares, regardless of mode — a bone socket whose
        // name resolves to nothing still gets a marker, sitting at the actor origin as the bake will place it.
        public void Rebuild(RigAsset rig, Material markerMaterial)
        {
            Dispose();
            if (rig == null || rig.sockets == null || rig.sockets.Count == 0)
            {
                return;
            }

            rootObject = new GameObject("ClipPreviewSockets");
            rootObject.hideFlags = HideFlags.HideAndDontSave;
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket == null)
                {
                    continue;
                }

                GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                markerObject.name = string.IsNullOrEmpty(socket.displayName)
                    ? "Socket " + socket.Id.Value.ToString()
                    : socket.displayName;
                markerObject.hideFlags = HideFlags.HideAndDontSave;
                markerObject.transform.SetParent(rootObject.transform, false);
                markerObject.transform.localScale =
                    new Vector3(MarkerSize, MarkerSize, MarkerSize);

                // The collider goes but the renderer stays: picking is done by the window's own
                // raycast against renderer bounds, and a physics collider in a preview scene is
                // never queried anyway.
                Collider markerCollider = markerObject.GetComponent<Collider>();
                if (markerCollider != null)
                {
                    Object.DestroyImmediate(markerCollider);
                }

                MeshRenderer markerRenderer = markerObject.GetComponent<MeshRenderer>();
                if (markerRenderer != null)
                {
                    markerRenderer.sharedMaterial = markerMaterial;
                    markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    markerRenderer.receiveShadows = false;
                    markerRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    markerRenderer.reflectionProbeUsage =
                        UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }

                sockets.Add(socket);
                markers.Add(markerObject.transform);
                attachments.Add(null);
            }

            RebuildAttachments();
        }

        // Instantiates each socket's preview attachment, replacing whatever was there. Parented
        // with worldPositionStays: false, then its local transform is zeroed rather than trusted —
        // a prefab authored ten metres from its own origin would otherwise hang that far off the hand.
        public void RebuildAttachments()
        {
            for (int index = 0; index < sockets.Count; index++)
            {
                if (attachments[index] != null)
                {
                    Object.DestroyImmediate(attachments[index]);
                    attachments[index] = null;
                }

                GameObject source = GetPreviewAttachment(sockets[index]);
                if (source == null)
                {
                    continue;
                }

                GameObject instance = Object.Instantiate(source, markers[index], false);
                instance.name = source.name + " (preview)";
                SetHideFlagsRecursively(instance, HideFlags.HideAndDontSave);
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

                // Undoes the marker's own scale, which is a display size for the cube and has no
                // business shrinking the sword to six centimetres.
                instance.transform.localScale = new Vector3(
                    1f / MarkerSize, 1f / MarkerSize, 1f / MarkerSize);

                StripColliders(instance);
                attachments[index] = instance;
            }
        }

        /// <summary>The prefab a socket previews with, or null. Editor-only authoring data.</summary>
        private static GameObject GetPreviewAttachment(SocketDefinition socket)
        {
            return socket != null ? socket.previewAttachment : null;
        }

        private static void SetHideFlagsRecursively(GameObject root, HideFlags flags)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < all.Length; index++)
            {
                all[index].gameObject.hideFlags = flags;
            }
        }

        private static void StripColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Object.DestroyImmediate(colliders[index]);
            }
        }

        // Places every marker on the pose its socket resolves to this frame. Call after the whole
        // rig has been posed, never between parts, or a marker shows the previous frame's pose.
        /// <param name="rigMirror">Source of part transforms, for rig-target sockets.</param>
        /// <param name="skeletonMirror">Source of bone transforms, for bone sockets.</param>
        public void UpdateMarkers(PreviewRigMirror rigMirror, PreviewSkeletonMirror skeletonMirror)
        {
            for (int index = 0; index < sockets.Count; index++)
            {
                SocketDefinition socket = sockets[index];
                Transform follow = ResolveFollowedTransform(socket, rigMirror, skeletonMirror);

                Vector3 basePosition = Vector3.zero;
                Quaternion baseRotation = Quaternion.identity;
                if (follow != null)
                {
                    basePosition = follow.localPosition;
                    baseRotation = follow.localRotation;
                }

                // The same composition SocketResolveSystem performs: the offset is expressed in the
                // followed thing's space, so it is rotated by that rotation before being added.
                markers[index].localPosition =
                    basePosition + baseRotation * socket.localPosition;
                markers[index].localRotation =
                    baseRotation * Quaternion.Euler(socket.localEulerAngles);
            }
        }

        /// <summary>The transform a socket follows this frame, or null when nothing resolves.</summary>
        public Transform GetFollowedTransform(
            SocketDefinition socket,
            PreviewRigMirror rigMirror,
            PreviewSkeletonMirror skeletonMirror)
        {
            return ResolveFollowedTransform(socket, rigMirror, skeletonMirror);
        }

        /// <summary>The transform a socket follows this frame, or null when nothing resolves.</summary>
        private static Transform ResolveFollowedTransform(
            SocketDefinition socket,
            PreviewRigMirror rigMirror,
            PreviewSkeletonMirror skeletonMirror)
        {
            if (socket.mode == SocketAttachMode.RigTarget)
            {
                return rigMirror != null ? rigMirror.GetPartTransform(socket.targetId) : null;
            }

            Transform bone;
            if (skeletonMirror != null && skeletonMirror.TryGetBone(socket.boneName, out bone))
            {
                return bone;
            }
            return null;
        }

        /// <summary>Whether a socket's binding resolves to something in the preview right now.</summary>
        public bool IsResolved(
            SocketDefinition socket,
            PreviewRigMirror rigMirror,
            PreviewSkeletonMirror skeletonMirror)
        {
            return socket != null
                && ResolveFollowedTransform(socket, rigMirror, skeletonMirror) != null;
        }

        private int IndexOf(uint socketId)
        {
            for (int index = 0; index < sockets.Count; index++)
            {
                if (sockets[index] != null && sockets[index].Id.Value == socketId)
                {
                    return index;
                }
            }
            return -1;
        }

        /// <summary>Destroys the markers, their attachments and the shared root. Idempotent.</summary>
        public void Dispose()
        {
            for (int index = 0; index < attachments.Count; index++)
            {
                if (attachments[index] != null)
                {
                    Object.DestroyImmediate(attachments[index]);
                }
            }
            attachments.Clear();

            if (rootObject != null)
            {
                Object.DestroyImmediate(rootObject);
                rootObject = null;
            }
            markers.Clear();
            sockets.Clear();
        }
    }
}
