using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Night ambience around the cab: a wind loop that rises and falls in gusts, timber creaks from the cab's walls
    /// when a gust hits, and an owl somewhere out in the forest now and then.
    /// </summary>
    public class AmbienceDirector : MonoBehaviour
    {
        [Tooltip("Centre of the cab (the tower's Markers/CabCentre). Empty = found by name, else this object.")]
        public Transform cabCentre;

        [Header("Wind")]
        public SoundCue wind;
        [Range(0f, 1f)] public float windVolume = 0.15f;
        [Tooltip("How much gusts swing the wind's volume.")]
        [Range(0f, 1f)] public float gustDepth = 0.55f;
        [Tooltip("Gusts per second, roughly.")]
        public float gustSpeed = 0.07f;

        [Header("Creaks (during strong gusts)")]
        public SoundCue[] creaks;
        [Range(0f, 1f)] public float creakGustThreshold = 0.62f;
        public Vector2 creakGap = new Vector2(3f, 9f);
        [Tooltip("Creaks come from points this far from the cab's centre (its walls are ~3.6 m out).")]
        public float creakRadius = 3.4f;

        [Header("Owl")]
        public SoundCue owl;
        public Vector2 owlInterval = new Vector2(25f, 60f);
        public Vector2 owlDistance = new Vector2(35f, 70f);

        /// <summary>Current gust strength, 0..1.</summary>
        public float Gust { get; private set; }

        AudioSource _wind;
        float _nextCreak, _nextOwl, _seed;

        void Start()
        {
            if (cabCentre == null)
            {
                var m = GameObject.Find("CabCentre");
                cabCentre = m != null ? m.transform : transform;
            }
            _seed = Random.value * 100f;
            if (wind.IsSet)
            {
                var go = new GameObject("Wind");
                go.transform.SetParent(transform, false);
                _wind = go.AddComponent<AudioSource>();
                _wind.clip = wind.clip;
                _wind.loop = true;
                _wind.spatialBlend = 0f;                       // all around the cab
                _wind.pitch = wind.pitch <= 0f ? 1f : wind.pitch;
                _wind.volume = 0f;
                _wind.time = Random.Range(0f, wind.clip.length * 0.9f);
                _wind.Play();
            }
            _nextCreak = Time.time + Random.Range(creakGap.x, creakGap.y);
            _nextOwl = Time.time + Random.Range(owlInterval.x * 0.4f, owlInterval.y * 0.6f);
        }

        void Update()
        {
            float t = Time.time * gustSpeed;
            Gust = Mathf.Clamp01(Mathf.PerlinNoise(t, _seed) * 0.75f + Mathf.PerlinNoise(t * 3.1f, _seed + 7f) * 0.35f);
            if (_wind != null) _wind.volume = windVolume * Mathf.Lerp(1f - gustDepth, 1f, Gust);

            if (creaks != null && creaks.Length > 0 && Gust >= creakGustThreshold && Time.time >= _nextCreak)
            {
                _nextCreak = Time.time + Random.Range(creakGap.x, creakGap.y);
                Vector2 d = Random.insideUnitCircle.normalized * creakRadius;
                Vector3 p = cabCentre.position + new Vector3(d.x, Random.Range(0.4f, 2.6f), d.y);
                Sfx.Play(creaks[Random.Range(0, creaks.Length)], p, 1f, 14f, Mathf.Lerp(0.6f, 1f, Gust), 2f);
            }

            if (owl.IsSet && Time.time >= _nextOwl)
            {
                _nextOwl = Time.time + Random.Range(owlInterval.x, owlInterval.y);
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(owlDistance.x, owlDistance.y);
                Vector3 p = new Vector3(cabCentre.position.x + d.x, cabCentre.position.y - 15f, cabCentre.position.z + d.y);
                Sfx.Play(owl, p, 1f, 160f, 1f, 18f);     // far off, but carries
            }
        }
    }
}
