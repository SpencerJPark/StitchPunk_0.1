using UnityEngine;

namespace PaintedGround
{
    /// Cheap campfire flicker: intensity and a small positional jitter, smoothed.
    [RequireComponent(typeof(Light))]
    public class CampfireFlicker : MonoBehaviour
    {
        public float baseIntensity = 6f;
        public float flickerAmount = 0.25f;
        public float speed = 9f;
        public float jitter = 0.08f;

        Light _light;
        Vector3 _home;
        float _seed;

        void Awake()
        {
            _light = GetComponent<Light>();
            _home = transform.localPosition;
            _seed = Random.value * 100f;
            baseIntensity = _light.intensity;
        }

        void Update()
        {
            float t = Time.time * speed + _seed;
            float n = Mathf.PerlinNoise(t, _seed) * 0.6f + Mathf.PerlinNoise(t * 2.3f, _seed + 7f) * 0.4f;
            _light.intensity = baseIntensity * (1f + (n - 0.5f) * 2f * flickerAmount);
            transform.localPosition = _home + new Vector3(
                (Mathf.PerlinNoise(t * 0.7f, 3f) - 0.5f) * jitter,
                (Mathf.PerlinNoise(t * 0.9f, 5f) - 0.5f) * jitter,
                (Mathf.PerlinNoise(t * 0.8f, 9f) - 0.5f) * jitter);
        }
    }
}
