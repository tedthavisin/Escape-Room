using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Keeps a world-space snowfall particle system centred over whoever is playing: the emitter box rides
    /// <see cref="heightAbove"/> metres above the main camera (desktop explorer or VR rig), so it snows on the path, on
    /// the stairs and outside the cab windows without simulating flakes over the whole map. Flakes already falling stay
    /// where they are as you move; fog hides the edge of the volume. A trigger box over the cab interior (Trigger
    /// module, Inside = Kill) keeps the flakes out of the cab.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class SnowFollow : MonoBehaviour
    {
        [Tooltip("How far above the camera the flakes are emitted.")]
        public float heightAbove = 12f;
        [Tooltip("0 = no snow, 1 = the builder's light snowfall. Scales the emission rate.")]
        [Range(0f, 2f)] public float intensity = 1f;
        public bool snowing = true;
        [Tooltip("If the camera jumps further than this in one frame (a teleport), re-settle the snow around it at once " +
                 "instead of waiting ~10 s for new flakes to fall into view.")]
        public float jumpDistance = 10f;
        [Tooltip("Wind carries the flakes sideways as they fall, which would leave the upwind side of the player bare. " +
                 "Every this-many seconds the emitter is shifted upwind so the flakes at the player's height stay centred on them.")]
        public float recentreInterval = 1.5f;

        ParticleSystem _ps;
        ParticleSystem.Particle[] _buf;
        float _baseRate = -1f;
        float _appliedRate = -1f;
        bool _placed;
        Vector3 _upwind;          // horizontal offset of the emitter from the camera, to cancel the wind drift
        float _nextRecentre;

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            _baseRate = _ps.emission.rateOverTimeMultiplier;
        }

        /// <summary>Start or stop the snowfall. Flakes already in the air finish falling.</summary>
        public void SetSnowing(bool on)
        {
            snowing = on;
            ApplyRate();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                if (Time.time >= _nextRecentre) Recentre(cam.transform.position);
                Vector3 target = cam.transform.position + Vector3.up * heightAbove + _upwind;
                bool jumped = _placed && (target - transform.position).sqrMagnitude > jumpDistance * jumpDistance;
                bool first = !_placed;
                transform.position = target;
                _placed = true;
                // first frame (the camera may not start where the builder put the emitter) or a teleport: fill the
                // volume around the new spot straight away
                if ((jumped || first) && snowing) Resettle();
            }
            ApplyRate();
        }

        /// <summary>
        /// Measure where the flakes around the camera's height actually are (wind drift) and nudge the emitter the other
        /// way, damped and a couple of metres at a time so it never reads as a jump.
        /// </summary>
        void Recentre(Vector3 camPos)
        {
            _nextRecentre = Time.time + recentreInterval;
            if (_ps == null) return;
            if (_buf == null || _buf.Length < _ps.main.maxParticles) _buf = new ParticleSystem.Particle[_ps.main.maxParticles];
            int n = _ps.GetParticles(_buf);
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = _buf[i].position;
                if (Mathf.Abs(p.y - camPos.y) > 4f) continue;   // only the band the player sees
                sum += p;
                count++;
            }
            if (count < 50) return;
            Vector3 drift = sum / count - camPos;
            drift.y = 0f;
            Vector3 step = Vector3.ClampMagnitude(-drift * 0.5f, 2f);
            _upwind = Vector3.ClampMagnitude(_upwind + step, 25f);
        }

        void Resettle()
        {
            if (_ps == null) return;
            float lifetime = _ps.main.startLifetime.constantMax;
            _ps.Simulate(lifetime, true, true);   // restart + fast-forward a full flake lifetime at the new position
            _ps.Play(true);
        }

        void ApplyRate()
        {
            if (_ps == null || _baseRate < 0f) return;
            float rate = snowing ? _baseRate * Mathf.Max(0f, intensity) : 0f;
            if (Mathf.Approximately(rate, _appliedRate)) return;
            _appliedRate = rate;
            var emission = _ps.emission;
            emission.rateOverTimeMultiplier = rate;
        }
    }
}
