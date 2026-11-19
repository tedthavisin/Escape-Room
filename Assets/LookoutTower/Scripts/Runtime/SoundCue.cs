using System;
using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// A sound to play: a clip, or just part of one (the CC0 recordings in Audio/ often hold several takes, e.g. a window
    /// opening and then closing), with volume and pitch. Play it with <see cref="Sfx.Play"/>.
    /// </summary>
    [Serializable]
    public struct SoundCue
    {
        public AudioClip clip;
        [Tooltip("Seconds into the clip to start from.")]
        public float start;
        [Tooltip("Seconds to play (of the clip's own time). 0 = to the end.")]
        public float length;
        [Range(0f, 1f)] public float volume;
        [Tooltip("Playback speed: below 1 is slower and deeper.")]
        public float pitch;
        [Tooltip("Random +/- added to the pitch each time, so repeats don't sound identical.")]
        public float pitchJitter;

        public bool IsSet => clip != null;

        public static SoundCue Of(AudioClip clip, float start = 0f, float length = 0f, float volume = 1f, float pitch = 1f,
                                  float pitchJitter = 0f)
            => new SoundCue { clip = clip, start = start, length = length, volume = volume, pitch = pitch, pitchJitter = pitchJitter };
    }

    /// <summary>Plays <see cref="SoundCue"/>s from a small pool of audio sources, fading the ends so cuts don't click.</summary>
    public static class Sfx
    {
        const int PoolSize = 12;
        static SfxVoice[] s_Pool;
        static int s_Next;

        /// <summary>
        /// Plays the cue at <paramref name="position"/>. <paramref name="spatial"/> 1 = fully 3D, 0 = in the player's ears.
        /// Full volume within <paramref name="minDistance"/> (default: 1 m), fading out to <paramref name="maxDistance"/>.
        /// Returns the voice's AudioSource (or null if the cue has no clip).
        /// </summary>
        public static AudioSource Play(in SoundCue cue, Vector3 position, float spatial = 1f, float maxDistance = 25f,
                                       float volumeScale = 1f, float minDistance = -1f)
        {
            if (!cue.IsSet) return null;
            var voice = NextVoice();
            voice.transform.position = position;
            var src = voice.Source;
            src.spatialBlend = spatial;
            src.maxDistance = maxDistance;
            src.minDistance = minDistance > 0f ? minDistance : Mathf.Min(1f, maxDistance * 0.1f);
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            float pitch = (cue.pitch <= 0f ? 1f : cue.pitch) + UnityEngine.Random.Range(-cue.pitchJitter, cue.pitchJitter);
            voice.Begin(cue.clip, cue.start, cue.length, (cue.volume <= 0f ? 1f : cue.volume) * volumeScale, Mathf.Max(0.1f, pitch));
            return src;
        }

        static SfxVoice NextVoice()
        {
            if (s_Pool == null || s_Pool[0] == null)
            {
                var root = new GameObject("SFX Voices");
                s_Pool = new SfxVoice[PoolSize];
                for (int i = 0; i < PoolSize; i++)
                {
                    var go = new GameObject("Voice " + i);
                    go.transform.SetParent(root.transform, false);
                    var src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.dopplerLevel = 0f;
                    s_Pool[i] = go.AddComponent<SfxVoice>();
                }
            }
            // prefer an idle voice; otherwise steal the oldest
            for (int i = 0; i < PoolSize; i++)
            {
                var v = s_Pool[(s_Next + i) % PoolSize];
                if (!v.Source.isPlaying) { s_Next = (s_Next + i + 1) % PoolSize; return v; }
            }
            var steal = s_Pool[s_Next];
            s_Next = (s_Next + 1) % PoolSize;
            return steal;
        }
    }
}
