using UnityEngine;

[RequireComponent(typeof(Transform))]
public class VehicleObstacleAdapter : MonoBehaviour
{
    public float obstacleRadius = 0.7f;
    public int   team = 0; // optional grouping

    public Rigidbody rb;   // optional
    DynamicObstacleRegistry.Item _item;
    Vector3 _lastPos;

    void Reset() { rb = GetComponent<Rigidbody>(); }

    void OnEnable()
    {
        _item = DynamicObstacleRegistry.Register(transform, obstacleRadius, team);
        _lastPos = transform.position;
    }

    void OnDisable()
    {
        if (_item != null) DynamicObstacleRegistry.Unregister(_item);
        _item = null;
    }

    void LateUpdate()
    {
        Vector3 vel = rb ? rb.linearVelocity : (transform.position - _lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
        DynamicObstacleRegistry.UpdateVelocity(_item, vel);
        _lastPos = transform.position;
    }
}
