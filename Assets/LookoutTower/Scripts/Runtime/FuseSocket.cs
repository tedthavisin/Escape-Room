using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// A place a <see cref="Fuse"/> can go: a threaded slot in the fuse panel, or a storage spot (the spare-fuse tin,
    /// the parts crate). This transform is the seat: the fuse's base contact goes on its origin and the fuse points
    /// along its local +Y (out of the slot).
    /// </summary>
    [DisallowMultipleComponent]
    public class FuseSocket : MonoBehaviour, IInteractable
    {
        [Tooltip("Shown in prompts, e.g. \"slot 1\", \"the tin\".")]
        public string spotName = "slot 1";
        [Tooltip("Storage spots just set the fuse down; panel slots screw it in.")]
        public bool storage;
        [Tooltip("The panel this slot belongs to (empty for storage). Changing fuses with the main switch on gets you a zap.")]
        public FuseBoxPanel panel;
        [Tooltip("Fuse sitting here when the scene starts.")]
        public Fuse startFuse;

        [Header("Screwing in")]
        public float screwSeconds = 0.5f;
        public float screwTurns = 2.5f;
        [Tooltip("How far out of the slot the fuse starts its last turns (metres).")]
        public float screwTravel = 0.012f;

        [Tooltip("After a fuse has been put in (once it's seated) or taken out.")]
        public UnityEvent<FuseSocket> onChanged = new UnityEvent<FuseSocket>();

        public Fuse Current { get; private set; }
        public bool IsBusy => _animating != null;

        Fuse _animating;
        float _t;
        bool _inserting;

        public string Prompt
        {
            get
            {
                var held = FuseHands.Held;
                if (IsBusy) return "";
                if (Current == null)
                {
                    if (held == null) return storage ? $"Nothing in {spotName}" : $"{Capitalise(spotName)} is empty";
                    return storage ? $"Put the {held.Describe()} in {spotName}" : $"Screw the {held.Describe()} into {spotName}";
                }
                if (held == null) return storage ? $"Take the {Current.Describe()}" : $"Unscrew the {Current.Describe()}";
                if (storage && FuseHands.HasRoom) return $"Take the {Current.Describe()} too";
                return storage ? $"Swap for the {Current.Describe()}" : $"Swap the {held.Describe()} in for the {Current.Describe()}";
            }
        }

        void Awake()
        {
            if (startFuse != null) Insert(startFuse, animate: false, notify: false);
        }

        public void Interact()
        {
            if (IsBusy) return;
            if (!storage && panel != null && panel.MainOn)
            {
                panel.LiveTouch(this);
                return;
            }
            if (Current == null)
            {
                var f = FuseHands.TakeHeld();
                if (f != null) Insert(f, animate: true);
            }
            else FuseHands.PickUp(Current, swapIntoSource: !storage);   // slot: held one goes in; tin/crate: pocket it
        }

        /// <summary>Seat a fuse here (it must not be in another socket or in hand).</summary>
        public void Insert(Fuse f, bool animate = true, bool notify = true)
        {
            if (f == null) return;
            if (Current != null && Current != f) Release(Current, notify: false);
            Current = f;
            f.Socket = this;
            f.transform.SetParent(transform, true);
            f.SetCollidersEnabled(true);
            if (!animate || !Application.isPlaying)
            {
                f.transform.localPosition = Vector3.zero;
                f.transform.localRotation = Quaternion.identity;
                if (notify) onChanged.Invoke(this);
                return;
            }
            _animating = f;
            _inserting = notify;
            _t = 0f;
            PuzzleSfx.Play(storage ? PuzzleSfx.SetDown : PuzzleSfx.Screw, transform.position, storage ? 0.5f : 0.7f);
            Step(0f);
        }

        /// <summary>Take the fuse out of here (the caller puts it somewhere: hand or another socket).</summary>
        public void Release(Fuse f, bool notify = true)
        {
            if (f == null || f != Current) return;
            if (_animating == f) FinishAnimation();
            Current = null;
            f.Socket = null;
            if (!storage) PuzzleSfx.Play(PuzzleSfx.Screw, transform.position, 0.5f);
            if (notify) onChanged.Invoke(this);
        }

        void Update()
        {
            if (_animating == null) return;
            _t += Time.deltaTime / Mathf.Max(0.05f, storage ? 0.18f : screwSeconds);
            Step(_t);
            if (_t >= 1f) FinishAnimation();
        }

        void Step(float t)
        {
            float k = Mathf.Clamp01(t);
            var tr = _animating.transform;
            if (storage)
            {
                float e = 1f - (1f - k) * (1f - k);
                tr.localPosition = Vector3.up * (0.03f * (1f - e));
                tr.localRotation = Quaternion.Slerp(tr.localRotation, Quaternion.identity, e);
            }
            else
            {
                // spin in along the axis: the threads pull it home over the last few turns
                tr.localPosition = Vector3.up * (screwTravel * (1f - k));
                tr.localRotation = Quaternion.AngleAxis(-360f * screwTurns * (1f - k), Vector3.up);
            }
        }

        void FinishAnimation()
        {
            var f = _animating;
            _animating = null;
            if (f == null) return;
            f.transform.localPosition = Vector3.zero;
            f.transform.localRotation = Quaternion.identity;
            if (_inserting) onChanged.Invoke(this);
        }

        static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void OnDrawGizmos()
        {
            Gizmos.color = storage ? new Color(0.6f, 0.9f, 0.6f, 0.8f) : new Color(1f, 0.75f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.012f);
            Gizmos.DrawLine(transform.position, transform.position + transform.up * 0.04f);
        }
    }
}
