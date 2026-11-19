using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Swings a compass needle round to north (+Z in the tower's world, the wall with Window_N2) and keeps it there
    /// however the compass is turned or carried, with a little overshoot and wobble like a real damped needle.
    /// Put it on the needle; the needle's local +Z is the end that points north, local +Y is up out of the dial.
    /// </summary>
    public class CompassNeedle : MonoBehaviour
    {
        [Tooltip("Points north. Empty = world +Z.")]
        public Transform northReference;
        [Tooltip("How stiffly the needle is pulled to north, and how quickly its swing dies away.")]
        public float stiffness = 60f, damping = 5f;
        [Tooltip("Degrees off north the needle starts at, so it visibly settles when the scene starts.")]
        public float startOffset = 35f;

        float _angle, _velocity;   // needle yaw on the dial, degrees, relative to the dial's +Z

        void OnEnable()
        {
            _angle = TargetAngle() + startOffset;
            _velocity = 0f;
            Apply();
        }

        void Update()
        {
            float error = Mathf.DeltaAngle(_angle, TargetAngle());
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _velocity += (error * stiffness - _velocity * damping) * dt;
            _angle += _velocity * dt;
            Apply();
        }

        /// <summary>North's direction as a yaw on the dial, measured from the dial's +Z.</summary>
        float TargetAngle()
        {
            var dial = transform.parent != null ? transform.parent : transform;
            Vector3 north = northReference != null ? northReference.forward : Vector3.forward;
            Vector3 local = dial.InverseTransformDirection(north);
            local.y = 0f;
            if (local.sqrMagnitude < 1e-6f) return _angle;            // compass on its side: leave the needle be
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        void Apply() => transform.localRotation = Quaternion.Euler(0f, _angle, 0f);
    }
}
