using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// Swings a door, trapdoor or window around its pivot. The GameObject's pivot sits on the hinge line,
    /// so the only setup is the hinge axis (local space) and how far it opens.
    /// Lock it from puzzle scripts with Lock()/Unlock() and listen to the UnityEvents.
    /// </summary>
    [DisallowMultipleComponent]
    public class HingedOpenable : MonoBehaviour, IInteractable
    {
        [Tooltip("Hinge axis in this object's local space. The pivot is already on the hinge.")]
        public Vector3 localAxis = Vector3.up;

        [Tooltip("Rotation in degrees when fully open. The sign picks the swing direction.")]
        public float openAngle = 90f;

        [Tooltip("Swing speed in degrees per second.")]
        public float speed = 110f;

        public bool startOpen;
        public bool locked;

        [Tooltip("Name used in the on-screen prompt, e.g. \"door\", \"trapdoor\".")]
        public string displayName = "door";

        [Tooltip("Pause the swing rather than pushing through a player (any CharacterController) standing in the way, " +
                 "e.g. someone on a trapdoor or with their head up through the hatch. Uses this object's BoxCollider.")]
        public bool stopAtCharacters = true;

        [Tooltip("Only a character standing in the opening itself (on a closed trapdoor, or on the ladder with their " +
                 "head up through the hatch) stops the swing; someone standing beside it doesn't. For trapdoors, where " +
                 "the player closing the hatch from above is usually standing right at its edge.")]
        public bool onlyCharactersInOpening;

        [Tooltip("Optional: plays when it starts opening/closing or when someone tries it while locked.")]
        public AudioSource audioSource;
        public AudioClip openClip, closeClip, lockedClip;

        [Header("Sounds (played at the leaf; used instead of the clips above when set)")]
        public SoundCue openSound;
        public SoundCue closeSound;
        [Tooltip("When it shuts fully: the latch or the lid landing.")]
        public SoundCue closedSound;
        public SoundCue lockedSound;

        public UnityEvent onOpened = new UnityEvent();
        public UnityEvent onClosed = new UnityEvent();
        public UnityEvent onLockedAttempt = new UnityEvent();

        Quaternion _closedRotation;
        float _current;   // 0 = closed, 1 = open
        float _target;
        bool _init;
        BoxCollider _leaf;
        static readonly Collider[] s_Hits = new Collider[16];

        public bool IsOpen => _target > 0.5f;
        public bool IsMoving => !Mathf.Approximately(_current, _target);
        public float OpenAmount => _current;
        /// <summary>True while the swing is paused because a character is in the way.</summary>
        public bool IsBlocked { get; private set; }

        public string Prompt => locked ? $"{Capitalise(displayName)} is locked"
            : IsBlocked ? "Something is in the way"
            : (IsOpen ? $"Close {displayName}" : $"Open {displayName}");

        void Awake() => Init();

        void Init()
        {
            if (_init) return;
            _init = true;
            _closedRotation = transform.localRotation;
            _current = _target = startOpen ? 1f : 0f;
            Apply();
        }

        public void Interact() => Toggle();

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Returns false (and fires onLockedAttempt) if locked.</summary>
        public bool Open()
        {
            Init();
            if (locked)
            {
                onLockedAttempt.Invoke();
                Play(lockedSound, lockedClip);
                return false;
            }
            if (_target < 1f) Play(openSound, openClip);
            _target = 1f;
            return true;
        }

        public void Close()
        {
            Init();
            if (_target > 0f) Play(closeSound, closeClip);
            _target = 0f;
        }

        /// <summary>Jump straight to open/closed without animating (e.g. when restoring a save).</summary>
        public void SetOpenImmediate(bool open)
        {
            Init();
            _current = _target = open ? 1f : 0f;
            Apply();
        }

        public void Lock() => locked = true;
        public void Unlock() => locked = false;
        public void SetLocked(bool value) => locked = value;

        void Update()
        {
            if (!IsMoving) { IsBlocked = false; return; }
            float prev = _current;
            float step = speed / Mathf.Max(1f, Mathf.Abs(openAngle)) * Time.deltaTime;
            _current = Mathf.MoveTowards(_current, _target, step);
            Apply();
            IsBlocked = stopAtCharacters && LeafHitsCharacter();
            if (IsBlocked)
            {
                // someone is in the way: hold here and try again next frame
                _current = prev;
                Apply();
                return;
            }
            if (_current >= 1f && prev < 1f) onOpened.Invoke();
            if (_current <= 0f && prev > 0f)
            {
                Play(closedSound, null);
                onClosed.Invoke();
            }
        }

        void Apply()
        {
            float eased = Mathf.SmoothStep(0f, 1f, _current);
            Vector3 axis = localAxis.sqrMagnitude > 1e-6f ? localAxis.normalized : Vector3.up;
            transform.localRotation = _closedRotation * Quaternion.AngleAxis(openAngle * eased, axis);
        }

        bool LeafHitsCharacter()
        {
            if (_leaf == null && !TryGetComponent(out _leaf)) return false;
            var t = transform;
            Vector3 centre = t.TransformPoint(_leaf.center);
            Vector3 half = Vector3.Scale(_leaf.size, t.lossyScale) * 0.5f;
            half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
            int n = gameObject.scene.GetPhysicsScene().OverlapBox(centre, half, s_Hits, t.rotation, Physics.AllLayers,
                                                                 QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (s_Hits[i] is CharacterController cc && (!onlyCharactersInOpening || InOpening(cc))) return true;
            return false;
        }

        /// <summary>
        /// Is the character's centre inside the opening, i.e. within the closed leaf's outline seen along its thinnest
        /// axis? (Standing on a closed trapdoor, or in the shaft under the hatch.)
        /// </summary>
        bool InOpening(CharacterController cc)
        {
            var t = transform;
            Quaternion closedWorld = (t.parent != null ? t.parent.rotation : Quaternion.identity) * _closedRotation;
            Vector3 p = Quaternion.Inverse(closedWorld) * (cc.bounds.center - t.position);
            p = new Vector3(p.x / t.lossyScale.x, p.y / t.lossyScale.y, p.z / t.lossyScale.z) - _leaf.center;
            Vector3 half = _leaf.size * 0.5f + Vector3.one * 0.05f;
            Vector3 s = _leaf.size;
            int thin = s.x < s.y ? (s.x < s.z ? 0 : 2) : (s.y < s.z ? 1 : 2);
            for (int a = 0; a < 3; a++)
                if (a != thin && Mathf.Abs(p[a]) > half[a]) return false;
            return true;
        }

        void Play(in SoundCue cue, AudioClip clip)
        {
            if (cue.IsSet) Sfx.Play(cue, LeafCentre(), 1f, 18f);
            else if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        Vector3 LeafCentre()
        {
            if (_leaf == null) TryGetComponent(out _leaf);
            return _leaf != null ? transform.TransformPoint(_leaf.center) : transform.position;
        }

        static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void OnDrawGizmosSelected()
        {
            Vector3 axis = transform.TransformDirection(localAxis.sqrMagnitude > 1e-6f ? localAxis.normalized : Vector3.up);
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawLine(transform.position - axis * 0.6f, transform.position + axis * 0.6f);
            Gizmos.DrawWireSphere(transform.position, 0.04f);
        }
    }
}
