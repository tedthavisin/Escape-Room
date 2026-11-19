using UnityEngine;

namespace LookoutTower
{
    /// <summary>One pooled voice for <see cref="Sfx"/>: plays part of a clip with a short fade in and out.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class SfxVoice : MonoBehaviour
    {
        const float Fade = 0.04f;   // seconds of clip time

        AudioSource _src;
        float _start, _end, _volume;

        public AudioSource Source => _src != null ? _src : (_src = GetComponent<AudioSource>());

        public void Begin(AudioClip clip, float start, float length, float volume, float pitch)
        {
            var s = Source;
            s.Stop();
            s.clip = clip;
            s.loop = false;
            s.pitch = pitch;
            _start = Mathf.Clamp(start, 0f, Mathf.Max(0f, clip.length - 0.01f));
            _end = length > 0f ? Mathf.Min(clip.length, _start + length) : clip.length;
            _volume = volume;
            s.volume = _start > 0.001f ? 0f : volume;   // cut mid-clip: fade in
            s.time = _start;
            s.Play();
        }

        void Update()
        {
            var s = Source;
            if (!s.isPlaying) return;
            float t = s.time;
            if (t >= _end - 0.001f) { s.Stop(); return; }
            float fadeIn = _start > 0.001f ? Mathf.Clamp01((t - _start) / Fade) : 1f;
            float fadeOut = _end < s.clip.length - 0.001f ? Mathf.Clamp01((_end - t) / Fade) : 1f;
            s.volume = _volume * Mathf.Min(fadeIn, fadeOut);
        }
    }
}
