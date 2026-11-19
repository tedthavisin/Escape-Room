using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// Reads the compass bearing the fire finder's sight points at and says when it is on the reported smoke.
    /// Bearings are true-north degrees, clockwise (0 = north/+Z, 90 = east/+X). The sight turns in 15° steps
    /// (<see cref="TurnOnInteract"/>), so the target should be a multiple of 15.
    /// </summary>
    public class FireFinderSighting : MonoBehaviour
    {
        public TurnOnInteract turn;
        [Tooltip("The rotating alidade (FireFinder_Sight).")]
        public Transform sight;
        [Tooltip("Direction of the front sight (the tall vane with the hair) in the sight's local space.")]
        public Vector3 sightFrontLocal = Vector3.forward;
        [Tooltip("Bearing of the reported smoke. The tower builder places the map circle and the beacon from this.")]
        public float targetBearing = 105f;
        [Tooltip("How close (degrees) the sight has to be to count as on target.")]
        public float tolerance = 3f;

        [Tooltip("Plays when the sight comes onto the bearing, e.g. static from the radio.")]
        public SoundCue alignedSound;
        [Tooltip("Where the aligned sound comes from (e.g. the radio). Empty = the fire finder.")]
        public Transform alignedSoundAt;

        public UnityEvent onAligned = new UnityEvent();
        public UnityEvent onMisaligned = new UnityEvent();

        public bool IsAligned { get; private set; }

        /// <summary>Bearing the front sight points at right now (0..360, follows the turning animation).</summary>
        public float CurrentBearing
        {
            get
            {
                if (sight == null) return 0f;
                Vector3 d = sight.TransformDirection(sightFrontLocal);
                return Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
            }
        }

        void Update()
        {
            bool aligned = Mathf.Abs(Mathf.DeltaAngle(CurrentBearing, targetBearing)) <= tolerance;
            if (aligned == IsAligned) return;
            IsAligned = aligned;
            if (aligned)
            {
                Sfx.Play(alignedSound, (alignedSoundAt != null ? alignedSoundAt : transform).position, 1f, 15f);
                onAligned.Invoke();
            }
            else onMisaligned.Invoke();
        }
    }
}
