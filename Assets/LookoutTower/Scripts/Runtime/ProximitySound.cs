using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Plays a sound here when the player's head comes within <see cref="radius"/> (e.g. paper rustling as you step up
    /// to a note), then waits until they've moved away again before it can play again.
    /// </summary>
    public class ProximitySound : MonoBehaviour
    {
        public SoundCue sound;
        public float radius = 1.1f;
        [Tooltip("How much further than the radius the player must move away before it can play again.")]
        public float rearmMargin = 0.6f;

        bool _armed = true;
        Transform _head;

        void Update()
        {
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _head = cam.transform;
            }
            float d = Vector3.Distance(_head.position, transform.position);
            if (_armed && d <= radius)
            {
                _armed = false;
                Sfx.Play(sound, transform.position, 1f, 6f);
            }
            else if (!_armed && d > radius + rearmMargin) _armed = true;
        }
    }
}
