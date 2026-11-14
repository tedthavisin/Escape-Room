using UnityEngine;

namespace LookoutTower
{
    /// <summary>Gentle kerosene-lantern flicker. Set <see cref="sputter"/> from a horror event to make it gutter.</summary>
    public class LanternFlicker : MonoBehaviour
    {
        public Light lamp;
        public float baseIntensity = 1.6f;
        [Range(0f, 1f)] public float flicker = 0.12f;
        public float speed = 7f;
        [Tooltip("0 = calm flame, 1 = violently guttering.")]
        [Range(0f, 1f)] public float sputter;

        float _seed;

        void Awake()
        {
            if (lamp == null) lamp = GetComponentInChildren<Light>();
            _seed = Random.value * 100f;
        }

        void Update()
        {
            if (lamp == null) return;
            float t = Time.time * speed;
            float n = Mathf.PerlinNoise(t, _seed) * 0.7f + Mathf.PerlinNoise(t * 2.7f, _seed + 13f) * 0.3f;
            float k = 1f - flicker + 2f * flicker * n;
            if (sputter > 0f)
            {
                float drop = Mathf.PerlinNoise(t * 4f, _seed + 31f);
                k *= Mathf.Lerp(1f, drop < 0.45f ? 0.08f : 1f, sputter);
            }
            lamp.intensity = baseIntensity * k;
        }
    }
}
