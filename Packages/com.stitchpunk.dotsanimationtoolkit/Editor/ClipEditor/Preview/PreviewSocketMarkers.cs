// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// Draws every socket where it will actually be, and rides whatever the user pinned to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Both socket modes are previewed, which was not previously true.</strong> Rig-target
    /// sockets followed their part; bone sockets drew nothing, on the stated grounds that the bone
    /// they follow "exists only inside a VAT texture". That reasoning expired when the preview began
    /// instantiating the rigged prefab and posing its skeleton (amendment A42, phase B4) — the bone
    /// is right there, posed, every frame. A socket the author cannot see is one they tune by
    /// entering play mode and guessing, which is the workflow this window exists to remove.
    /// </para>
    /// <para>
    /// <strong>Composition matches <c>SocketResolveSystem</c> exactly</strong>: the followed
    /// transform's local pose, then the socket's own offset rotated into it. Composing differently
    /// here — reading a world matrix, say — would give a marker that agrees with the runtime in the
    /// common case and drifts in the rotated one, which is worse than not drawing it.
    /// </para>
    /// <para>
    /// Attachments are instantiated as children of the marker, so they inherit its pose for free and
    /// there is no second placement path to keep in agreement with the first.
    /// </para>
    /// </remarks>
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

        /// <summary>The socket a picked transform stands for, or false when it is not a marker.</summary>
        /// <remarks>
        /// Walks up from the picked transform, because a click usually lands on a child of an
        /// <em>attachment</em> — the blade of the sword, not the socket cube. Treating that as a
        /// miss would make every attached socket unselectable the moment it had geometry.
        /// </remarks>
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

        /// <summary>Rebuilds a marker per socket the rig declares.</summary>
        /// <remarks>
        /// Every socket gets one now, regardless of mode. A bone socket whose name resolves to
        /// nothing still gets a marker; it simply sits at the actor origin, which is exactly where
        /// the bake will put the attachment and therefore what the author needs to see.
        /// </remarks>
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

        /// <summary>
        /// Instantiates each socket's preview attachment, replacing whatever was there.
        /// </summary>
        /// <remarks>
        /// The instance is parented to the marker with <c>worldPositionStays: false</c> so it adopts
        /// the socket's pose exactly. Its own transform is then zeroed rather than trusted: a prefab
        /// authored ten metres from its own origin would otherwise hang that far off the hand, and
        /// the attachment's job here is to show where the socket is, not where the prefab was saved.
        /// </remarks>
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

        /// <summary>
        /// Places every marker on the pose its socket resolves to this frame.
        /// </summary>
        /// <param name="rigMirror">Source of part transforms, for rig-target sockets.</param>
        /// <param name="skeletonMirror">Source of bone transforms, for bone sockets.</param>
        /// <remarks>
        /// Call after the whole rig has been posed for the frame, never between parts. A marker
        /// placed before the thing it follows shows the previous frame's pose, which reads as the
        /// attachment lagging the hand rather than as an ordering mistake here.
        /// </remarks>
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
        /// <remarks>
        /// The window reports this rather than leaving the author to wonder why a marker is sitting
        /// on the origin — an unresolved bone name is the failure that otherwise surfaces as a
        /// weapon pinned to the actor's feet at run time.
        /// </remarks>
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
