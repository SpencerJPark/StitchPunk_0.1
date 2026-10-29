using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

// World marker over the drop-off of every accepted order: a beacon pole plus a camera-facing text line. Placeholder
// geometry until real marker art exists. Delivery progress ("1/2 Brutes") arrives with Phase 3.
public class TradeOrderMarkerPresenter : MonoBehaviour
{
    private const float PoleHeight = 3f;

    [SerializeField] private ClientLibrarySO library;

    private readonly Dictionary<int, Marker> markersByDropOffId = new Dictionary<int, Marker>();
    private readonly HashSet<int> seenDropOffIds = new HashSet<int>();
    private readonly List<int> staleDropOffIds = new List<int>();

    private EntityQuery boardQuery;
    private EntityQuery dropOffQuery;
    private World queryWorld;
    private Font labelFont;

    private sealed class Marker
    {
        public GameObject root;
        public TextMesh label;
    }

    private void LateUpdate()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (queryWorld != world)
        {
            queryWorld = world;
            boardQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TradeBoard>());
            dropOffQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TradeDropOff>());
        }

        if (boardQuery.IsEmptyIgnoreFilter)
            return;

        EntityManager entityManager = world.EntityManager;
        DynamicBuffer<TradeOrder> orders = entityManager.GetBuffer<TradeOrder>(boardQuery.GetSingletonEntity(), true);
        using (Unity.Collections.NativeArray<TradeDropOff> dropOffs = dropOffQuery.ToComponentDataArray<TradeDropOff>(Unity.Collections.Allocator.Temp))
        {
            seenDropOffIds.Clear();
            for (int dropOffIndex = 0; dropOffIndex < dropOffs.Length; dropOffIndex++)
            {
                TradeDropOff dropOff = dropOffs[dropOffIndex];
                string markerText = BuildMarkerText(orders, dropOff.dropOffId);
                if (markerText == null)
                    continue;

                seenDropOffIds.Add(dropOff.dropOffId);
                if (!markersByDropOffId.TryGetValue(dropOff.dropOffId, out Marker marker))
                {
                    marker = CreateMarker(dropOff.dropOffId);
                    markersByDropOffId.Add(dropOff.dropOffId, marker);
                }

                marker.root.transform.position = (Vector3)dropOff.standPoint;
                marker.label.text = markerText;
                FaceCamera(marker.label.transform);
            }
        }

        staleDropOffIds.Clear();
        foreach (KeyValuePair<int, Marker> pair in markersByDropOffId)
        {
            if (!seenDropOffIds.Contains(pair.Key))
                staleDropOffIds.Add(pair.Key);
        }
        foreach (int staleDropOffId in staleDropOffIds)
        {
            Destroy(markersByDropOffId[staleDropOffId].root);
            markersByDropOffId.Remove(staleDropOffId);
        }
    }

    private void OnDestroy()
    {
        foreach (Marker marker in markersByDropOffId.Values)
        {
            if (marker.root != null)
                Destroy(marker.root);
        }
        markersByDropOffId.Clear();
    }

    private string BuildMarkerText(DynamicBuffer<TradeOrder> orders, int dropOffId)
    {
        string text = null;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder order = orders[orderIndex];
            if (order.state != TradeOrderState.Accepted || order.dropOffId != dropOffId)
                continue;

            string line = $"{TradeTextLookup.ClientName(library, order.clientId)}\n{TradeTextLookup.Requirement(order)} · by {TradeTextLookup.DayName(order.dueDayOfWeek)}";
            text = text == null ? line : text + "\n" + line;
        }
        return text;
    }

    private Marker CreateMarker(int dropOffId)
    {
        GameObject root = new GameObject($"TradeMarker_{dropOffId}");
        root.transform.SetParent(transform, false);

        GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Pole";
        Destroy(pole.GetComponent<Collider>());
        pole.transform.SetParent(root.transform, false);
        pole.transform.localScale = new Vector3(0.2f, PoleHeight * 0.5f, 0.2f);
        pole.transform.localPosition = new Vector3(0f, PoleHeight * 0.5f, 0f);
        pole.GetComponent<Renderer>().material.color = new Color(1f, 0.8f, 0.2f);

        if (labelFont == null)
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(root.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, PoleHeight + 0.6f, 0f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.font = labelFont;
        label.fontSize = 48;
        label.characterSize = 0.08f;
        label.anchor = TextAnchor.LowerCenter;
        label.alignment = TextAlignment.Center;
        label.color = new Color(1f, 0.92f, 0.6f);
        labelObject.GetComponent<MeshRenderer>().sharedMaterial = labelFont.material;

        return new Marker { root = root, label = label };
    }

    private static void FaceCamera(Transform labelTransform)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            labelTransform.rotation = mainCamera.transform.rotation;
    }
}
