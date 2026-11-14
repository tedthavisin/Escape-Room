using UnityEngine;
using UnityEngine.Events;

namespace LookoutTower
{
    /// <summary>
    /// Rotates a target (e.g. the fire finder's sighting ring) by a fixed step each time it's used.
    /// Handy for "set the azimuth to 135 degrees" style puzzles: read CurrentAngle or listen to onTurned.
    /// </summary>
    public class TurnOnInteract : MonoBehaviour, IInteractable
    {
        public Transform target;
        [Tooltip("Rotation axis in the target's local space.")]
        public Vector3 localAxis = Vector3.up;
        public float stepDegrees = 15f;
        public float speed = 180f;
        public string prompt = "Turn the sight";

        public UnityEvent<float> onTurned = new UnityEvent<float>();

        Quaternion _start;
        float _angle, _shown;

        public float CurrentAngle => Mathf.Repeat(_angle, 360f);
        public string Prompt => prompt;

        void Awake()
        {
            if (target == null) target = transform;
            _start = target.localRotation;
        }

        public void Interact()
        {
            _angle += stepDegrees;
            onTurned.Invoke(CurrentAngle);
        }

        void Update()
        {
            if (Mathf.Approximately(_shown, _angle)) return;
            _shown = Mathf.MoveTowards(_shown, _angle, speed * Time.deltaTime);
            target.localRotation = _start * Quaternion.AngleAxis(_shown, localAxis.normalized);
        }
    }
}
