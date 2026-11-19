using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// Something a fuse-panel circuit switches: lights, glowing materials (bulb glass, the radio dial) and an optional
    /// hum. The "on" look is whatever the scene was authored with; while unpowered the lights are off and the
    /// emission is black. Powering up stutters for a moment like an old filament warming up.
    /// </summary>
    public class PoweredOutput : MonoBehaviour
    {
        public Light[] lights;
        [Tooltip("Renderers whose glowing material follows the power.")]
        public Renderer[] emissiveRenderers;
        [Tooltip("Only materials whose name starts with this glow (e.g. M_Bulb, M_DialGlow). Empty = every material with emission.")]
        public string emissiveMaterialPrefix = "M_Bulb";
        public bool startPowered;
        public bool flickerOnPowerUp = true;
        [Tooltip("Optional looping hum, played only while powered.")]
        public AudioSource hum;

        public UnityEvent onPowered = new UnityEvent();
        public UnityEvent onUnpowered = new UnityEvent();

        public bool IsPowered { get; private set; }

        float[] _intensity;
        Material[] _mats;
        Color[] _emission;
        float _flickerUntil;
        bool _init;

        void Awake() => Init();

        void Init()
        {
            if (_init) return;
            _init = true;
            if (lights == null) lights = new Light[0];
            _intensity = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) _intensity[i] = lights[i] != null ? lights[i].intensity : 0f;

            var mats = new System.Collections.Generic.List<Material>();
            var em = new System.Collections.Generic.List<Color>();
            if (emissiveRenderers != null)
                foreach (var r in emissiveRenderers)
                {
                    if (r == null) continue;
                    var shared = r.sharedMaterials;
                    var inst = r.materials;              // per-renderer copies so other props keep their look
                    for (int k = 0; k < inst.Length; k++)
                    {
                        var src = shared[k];
                        if (src == null || !src.HasProperty("_EmissionColor")) continue;
                        if (!string.IsNullOrEmpty(emissiveMaterialPrefix) && !src.name.StartsWith(emissiveMaterialPrefix)) continue;
                        mats.Add(inst[k]);
                        em.Add(src.GetColor("_EmissionColor"));
                    }
                }
            _mats = mats.ToArray();
            _emission = em.ToArray();
            if (hum != null)
            {
                if (hum.clip == null) hum.clip = PuzzleSfx.Hum;
                hum.loop = true;
                hum.playOnAwake = false;
                if (!startPowered) hum.Stop();
            }
            Apply(startPowered, 1f);
            IsPowered = startPowered;
        }

        public void SetPowered(bool on)
        {
            Init();
            if (on == IsPowered) return;
            IsPowered = on;
            if (on && flickerOnPowerUp) _flickerUntil = Time.time + 0.6f;
            Apply(on, 1f);
            if (hum != null)
            {
                if (on && !hum.isPlaying) hum.Play();
                else if (!on) hum.Stop();
            }
            (on ? onPowered : onUnpowered).Invoke();
        }

        void Update()
        {
            if (_flickerUntil <= 0f) return;
            if (!IsPowered) { _flickerUntil = 0f; return; }
            if (Time.time >= _flickerUntil)
            {
                _flickerUntil = 0f;
                Apply(true, 1f);
                return;
            }
            float left = (_flickerUntil - Time.time) / 0.6f;
            float n = Mathf.PerlinNoise(Time.time * 38f, 7.3f);
            Apply(true, n < 0.35f + 0.4f * left ? 0.08f : 1f);
        }

        void Apply(bool on, float k)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                lights[i].enabled = on;
                lights[i].intensity = _intensity[i] * k;
            }
            for (int i = 0; i < _mats.Length; i++)
            {
                var c = on ? _emission[i] * k : Color.black;
                _mats[i].SetColor("_EmissionColor", c);
                if (on) _mats[i].EnableKeyword("_EMISSION");
            }
        }

        void OnDestroy()
        {
            if (_mats == null) return;
            foreach (var m in _mats) if (m != null) Destroy(m);
        }
    }
}
