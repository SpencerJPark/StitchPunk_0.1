using UnityEngine;
using System.Collections.Generic;

public class VehicleWheelAnimator : MonoBehaviour
{
    [SerializeField] private List<WheelPair> wheelPairs;

    // internal angles for each mesh
    private float[] _angles;

    void Awake()
    {
        // one angle slot per mesh
        _angles = new float[wheelPairs.Count * 2];
    }

    public void UpdateWheels(float currentSpeed)
    {
        // each WheelPair holds two wheels (left & right)
        for (int i = 0; i < wheelPairs.Count; i++)
        {
            var pair = wheelPairs[i];
            AnimatePair(i * 2 + 0, currentSpeed, pair.left.radius,  pair.left.mesh);
            AnimatePair(i * 2 + 1, currentSpeed, pair.right.radius, pair.right.mesh);
        }
    }

    private void AnimatePair(int idx, float speed, float radius, Transform mesh)
    {
        if (radius <= 0f) return;

        // rpm = (speed / circumference) * 60
        float rpm = (speed / (2f * Mathf.PI * radius)) * 60f;
        float deltaAngle = rpm / 60f * 360f * Time.deltaTime; 
        _angles[idx] = (_angles[idx] + deltaAngle) % 360f;
        mesh.localRotation = Quaternion.Euler(_angles[idx], 0f, 0f);
    }

    [System.Serializable]
    public struct WheelPair
    {
        public WheelData left;
        public WheelData right;

        [System.Serializable]
        public struct WheelData
        {
            public Transform mesh;
            public float radius;
        }
    }
}
