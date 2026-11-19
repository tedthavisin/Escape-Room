using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// The ranger's answering lamp out in the forest. While the fire finder is sighted on the smoke it signals a hatch-code
    /// digit and its place: <see cref="slotFlashes"/> slow flashes (which digit of the code), a gap, <see cref="flashes"/>
    /// quick flashes (the digit itself), a long pause, and repeat. Each new sighting restarts at the first slow flash.
    /// The glow uses a fog-free shader so it reads through the night haze; the point light just lights the mast.
    /// </summary>
    public class SightingBeacon : MonoBehaviour
    {
        public FireFinderSighting sighting;
        [Tooltip("Billboard glow quad (BeaconGlow shader); turned to face the camera every frame.")]
        public Renderer glow;
        public Light lampLight;

        [Header("Which digit of the code (slow flashes)")]
        [Tooltip("Position in the hatch code, shown as slow flashes before the digit. 0 = skip them.")]
        public int slotFlashes = 2;
        public float slowOn = 0.9f;
        public float slowOff = 0.7f;
        [Tooltip("Dark gap between the slow (position) and quick (digit) flashes.")]
        public float groupGap = 1.4f;
        [Tooltip("Slow flashes are a little dimmer and warmer than the quick ones, so the two groups read differently.")]
        public Color slowTint = new Color(1f, 0.7f, 0.45f);
        [Range(0.2f, 1f)] public float slowBrightness = 0.65f;

        [Header("The digit (quick flashes)")]
        public int flashes = 7;
        public float onTime = 0.3f;
        public float offTime = 0.3f;
        [Tooltip("Dark pause after the digit before the pattern repeats.")]
        public float pause = 3f;

        [Tooltip("Debug: signal whether or not the sight is on target.")]
        public bool alwaysOn;

        [Tooltip("Fires as each flash starts; true for a slow (position) flash. Hook a click or buzz sound here.")]
        public UnityEvent<bool> onFlash = new UnityEvent<bool>();

        bool _active;
        float _t;
        float _baseLight = -1f;
        MaterialPropertyBlock _mpb;
        Color _baseGlow;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>True while the lamp is lit this instant (for tests and hints).</summary>
        public bool IsLit { get; private set; }
        /// <summary>True while the current flash belongs to the slow (position) group.</summary>
        public bool IsSlowFlash { get; private set; }

        /// <summary>Length of one full signal: slow group, gap, quick group, pause.</summary>
        public float CycleLength => SlowLength + (slotFlashes > 0 ? groupGap : 0f) + flashes * (onTime + offTime) + pause;
        float SlowLength => Mathf.Max(0, slotFlashes) * (slowOn + slowOff);

        void Start()
        {
            if (lampLight != null) _baseLight = lampLight.intensity;
            if (glow != null && glow.sharedMaterial != null && glow.sharedMaterial.HasProperty(ColorId))
                _baseGlow = glow.sharedMaterial.GetColor(ColorId);
            SetLit(false, false, true);
        }

        void Update()
        {
            bool active = alwaysOn || (sighting != null && sighting.IsAligned);
            if (active != _active)
            {
                _active = active;
                _t = 0f;   // a fresh sighting starts from the first slow flash
            }
            if (!_active) { SetLit(false, false); return; }

            _t += Time.deltaTime;
            bool lit = LitAt(_t, out bool slow);
            if (lit && !IsLit) onFlash.Invoke(slow);
            SetLit(lit, slow);
        }

        /// <summary>Whether the lamp is lit <paramref name="time"/> seconds into a sighting, and if so whether it's a
        /// slow (position) flash. The whole signal is a function of time, so it can be checked without playing.</summary>
        public bool LitAt(float time, out bool slow)
        {
            slow = false;
            float t = Mathf.Repeat(time, CycleLength);
            if (t < SlowLength)
            {
                slow = true;
                return Mathf.Repeat(t, slowOn + slowOff) < slowOn;
            }
            t -= SlowLength + (slotFlashes > 0 ? groupGap : 0f);
            float quick = onTime + offTime;
            return t >= 0f && t < flashes * quick && Mathf.Repeat(t, quick) < onTime;
        }

        void LateUpdate()
        {
            if (glow == null || !glow.enabled) return;
            var cam = Camera.main;
            if (cam == null) return;
            var toCam = cam.transform.position - glow.transform.position;
            if (toCam.sqrMagnitude > 1e-6f) glow.transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
        }

        void SetLit(bool lit, bool slow, bool force = false)
        {
            if (!force && lit == IsLit && slow == IsSlowFlash) return;
            IsLit = lit;
            IsSlowFlash = lit && slow;
            if (glow != null)
            {
                glow.enabled = lit;
                if (lit && _baseGlow != default)
                {
                    _mpb ??= new MaterialPropertyBlock();
                    Color c = IsSlowFlash ? _baseGlow * slowTint * slowBrightness : _baseGlow;
                    c.a = 1f;
                    _mpb.SetColor(ColorId, c);
                    glow.SetPropertyBlock(_mpb);
                }
            }
            if (lampLight != null)
            {
                lampLight.enabled = lit;
                if (_baseLight >= 0f) lampLight.intensity = IsSlowFlash ? _baseLight * slowBrightness : _baseLight;
            }
        }
    }
}
