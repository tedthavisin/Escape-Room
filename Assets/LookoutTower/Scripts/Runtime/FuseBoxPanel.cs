using System;
using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>One slot of the fuse panel and the things it powers.</summary>
    [Serializable]
    public class FuseCircuit
    {
        public string name = "Lights";
        [Tooltip("The rating this circuit needs, as printed on the wiring card.")]
        public int amps = 15;
        public FuseSocket socket;
        public PoweredOutput[] outputs = new PoweredOutput[0];

        [NonSerialized] public bool live;
    }

    /// <summary>
    /// The cab's fuse panel. Each circuit is live when the main switch is on and its slot holds an unblown fuse of
    /// exactly the right rating. Throwing the main with an over-rated fuse anywhere (one that wouldn't protect the
    /// wiring) sparks at that slot and kicks the switch back off; an under-rated fuse crackles and its circuit stays
    /// dead. Nothing is ever destroyed, so the puzzle can't be soft-locked.
    ///
    /// Hook the rest of the room up with <see cref="onSolved"/> (all circuits live), <see cref="onCircuitChanged"/>
    /// and each circuit's <see cref="PoweredOutput"/> events.
    /// </summary>
    [DisallowMultipleComponent]
    public class FuseBoxPanel : MonoBehaviour
    {
        public FuseCircuit[] circuits = new FuseCircuit[0];
        public HingedOpenable door;

        [Header("Main switch")]
        public Transform switchHandle;
        [Tooltip("Handle spindle in the handle's local space.")]
        public Vector3 handleAxis = Vector3.right;
        [Tooltip("Handle angle (degrees from how it was modelled) for ON and OFF.")]
        public float onAngle = -55f, offAngle = 55f;
        public float handleSpeed = 900f;
        public bool startOn;

        [Header("Effects")]
        public Transform sparkPoint;
        [Tooltip("Additive particle material for sparks.")]
        public Material sparkMaterial;
        [Tooltip("Optional. Plays the clunk / zap from here instead of a one-shot at the panel.")]
        public AudioSource audioSource;

        [Header("Events")]
        public UnityEvent onSolved = new UnityEvent();
        [Tooltip("Main switch thrown with a wrong fuse in: sparks and the switch kicks back.")]
        public UnityEvent onFault = new UnityEvent();
        public UnityEvent<bool> onMainSwitched = new UnityEvent<bool>();
        [Tooltip("(circuit index, live)")]
        public UnityEvent<int, bool> onCircuitChanged = new UnityEvent<int, bool>();

        public bool MainOn { get; private set; }
        public bool IsSolved { get; private set; }

        Quaternion _handleRest;
        float _handleAngle, _handleTarget;
        float _kickAt = -1f;
        FuseCircuit _kickCircuit;

        void Awake()
        {
            if (switchHandle != null) _handleRest = switchHandle.localRotation;
            _handleAngle = _handleTarget = startOn ? onAngle : offAngle;
            ApplyHandle();
            foreach (var c in circuits)
                if (c.socket != null)
                {
                    c.socket.panel = this;
                    c.socket.onChanged.AddListener(_ => Evaluate());
                }
        }

        void Start()
        {
            MainOn = startOn;
            Evaluate();
        }

        public void ToggleMain() => SetMain(!MainOn);

        public void SetMain(bool on)
        {
            if (_kickAt >= 0f) return;                        // already kicking back
            if (on == MainOn) return;
            Play(PuzzleSfx.Clunk, 0.9f);
            _handleTarget = on ? onAngle : offAngle;
            if (on && FindOverRated(out var bad))
            {
                // the handle goes over, then the panel faults and throws it back
                _kickCircuit = bad;
                _kickAt = Time.time + 0.22f;
                return;
            }
            MainOn = on;
            onMainSwitched.Invoke(on);
            if (on) CrackleUnderRated();
            Evaluate();
        }

        /// <summary>The player reached into a live panel.</summary>
        public void LiveTouch(FuseSocket socket)
        {
            Spark(socket != null ? socket.transform : sparkPoint, 12);
            PuzzleHints.Show("Zap! Switch the main off before changing fuses.");
        }

        bool FindOverRated(out FuseCircuit bad)
        {
            foreach (var c in circuits)
            {
                var f = c.socket != null ? c.socket.Current : null;
                if (f != null && !f.blown && f.amps > c.amps) { bad = c; return true; }
            }
            bad = null;
            return false;
        }

        void CrackleUnderRated()
        {
            foreach (var c in circuits)
            {
                var f = c.socket != null ? c.socket.Current : null;
                if (f == null || f.blown || f.amps >= c.amps) continue;
                Spark(c.socket.transform, 8);
                PuzzleHints.Show($"The {f.amps} A fuse in {c.socket.spotName} crackles. Too small for the {c.name.ToLowerInvariant()}.");
            }
        }

        /// <summary>Recompute which circuits are live (called whenever a fuse or the main switch changes).</summary>
        public void Evaluate()
        {
            bool all = circuits.Length > 0;
            for (int i = 0; i < circuits.Length; i++)
            {
                var c = circuits[i];
                var f = c.socket != null ? c.socket.Current : null;
                bool live = MainOn && f != null && !f.blown && f.amps == c.amps && (c.socket == null || !c.socket.IsBusy);
                all &= live;
                if (live == c.live) continue;
                c.live = live;
                foreach (var o in c.outputs) if (o != null) o.SetPowered(live);
                onCircuitChanged.Invoke(i, live);
            }
            if (all && !IsSolved)
            {
                IsSolved = true;
                PuzzleHints.Show("The lights hum to life.");
                onSolved.Invoke();
            }
        }

        void Update()
        {
            if (_kickAt >= 0f && Time.time >= _kickAt)
            {
                _kickAt = -1f;
                var slot = _kickCircuit != null && _kickCircuit.socket != null ? _kickCircuit.socket : null;
                Spark(slot != null ? slot.transform : sparkPoint, 30);
                Play(PuzzleSfx.Clunk, 0.7f);
                _handleTarget = offAngle;
                var f = slot != null ? slot.Current : null;
                PuzzleHints.Show(slot != null && f != null
                    ? $"Sparks from {slot.spotName} and the main switch kicks back off."
                    : "Sparks, and the main switch kicks back off.");
                onFault.Invoke();
                _kickCircuit = null;
            }
            if (!Mathf.Approximately(_handleAngle, _handleTarget))
            {
                _handleAngle = Mathf.MoveTowards(_handleAngle, _handleTarget, handleSpeed * Time.deltaTime);
                ApplyHandle();
            }
        }

        void ApplyHandle()
        {
            if (switchHandle == null) return;
            var axis = handleAxis.sqrMagnitude > 1e-6f ? handleAxis.normalized : Vector3.right;
            switchHandle.localRotation = _handleRest * Quaternion.AngleAxis(_handleAngle, axis);
        }

        void Spark(Transform at, int count)
        {
            if (at == null) at = transform;
            // spray out of the slot, towards the room
            Vector3 dir = at.up;
            if (sparkPoint != null && at != sparkPoint) dir = (at.up + Vector3.down * 0.3f).normalized;
            SparkFx.Burst(at.position + at.up * 0.02f, dir, sparkMaterial, count);
            Play(PuzzleSfx.Zap, 1f, at.position);
        }

        void Play(AudioClip clip, float vol, Vector3? at = null)
        {
            if (audioSource != null) audioSource.PlayOneShot(clip, vol);
            else PuzzleSfx.Play(clip, at ?? (switchHandle != null ? switchHandle.position : transform.position), vol);
        }
    }
}
