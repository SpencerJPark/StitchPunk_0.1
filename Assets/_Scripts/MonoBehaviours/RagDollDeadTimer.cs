using System;
using UnityEngine;

public class RagDollDeadTimer : MonoBehaviour, IUpdateObserver
{
    private float timer = 6f;
    private bool hasColliders = true;

    private void OnEnable() => UpdateManager.RegisterObserver(this);
    private void OnDisable() => UpdateManager.UnregisterObserver(this);
    
    public void ObservedUpdate()
    {
        timer -= Time.deltaTime;

        if (hasColliders && timer <= 3f)
        {
            foreach (CharacterJoint characterJoint in GetComponentsInChildren<CharacterJoint>())
            {
                Destroy(characterJoint);
            }
            foreach (Rigidbody rigidbody in GetComponentsInChildren<Rigidbody>())
            {
                Destroy(rigidbody);
            }
            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                Destroy(collider);
            }
            hasColliders = false;
        }

        if (timer <= 1f)
        {
            transform.position += Vector3.down * Time.deltaTime;
        }

        if (timer <= 0)
        {
            Destroy(gameObject);
        }
    }
}
